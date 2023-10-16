using BlazorDatasheet.DataStructures.Intervals;
using BlazorDatasheet.DataStructures.Search;
using BlazorDatasheet.DataStructures.Util;

namespace BlazorDatasheet.DataStructures.Store;

public class CumulativeRange1DStore : Range1DStore<double>
{
    public readonly double Default;

    private readonly List<int> _storedPositionStarts = new();
    private readonly List<int> _storedPositionEnds = new();
    private readonly List<double> _cumulativeValuesAtEnd = new();
    private readonly List<double> _cumulativeValuesAtStart = new();

    /// <summary>
    /// Stores/retrieves widths of ranges. Useful for setting column/row size, because
    /// we can calculate the x/y positions of a row/column as well as distances between rows/columns.
    /// Each range index has a particular size with the cumulative being the position at the START of the range
    /// E.g say we set the size of 0 to 20 and the size of [2,3] to 30
    /// We then have the following
    ///    0        1        2    3      4
    /// | 20 | | default |   30  30  |  default
    /// The cumulative of 0 is always zero, the cumulative of 1 is 20, the cumulative of 2 is 20 + default, for 3 = 20 + default + 30.
    /// </summary>
    /// <param name="default">The size of a range if it has not been explicitly set.</param>
    public CumulativeRange1DStore(double @default) : base(@default)
    {
        Default = @default;
    }

    public override List<(int stat, int end, double value)> Set(int start, int end, double value)
    {
        var res = base.Set(start, end, value);
        // update cumulative positions1
        UpdateCumulativePositons(start);
        return res;
    }

    public double GetSize(int position)
    {
        return this.Get(position);
    }

    private void UpdateCumulativePositons(int fromPosition)
    {
        // update all cumulative positions using all intervals
        ClearCumulativeData();

        var intervals = _intervals.GetAllIntervals();
        foreach (var interval in intervals)
        {
            var existingCumEnd = _cumulativeValuesAtEnd.Any();
            var cumEndPrev = _cumulativeValuesAtEnd.LastOrDefault();
            var posEndPrev = _storedPositionEnds.LastOrDefault();
            var intervalSize = interval.Length * interval.Data.Value;
            //     e s
            // [   ] [   ] intervals are NON overlapping.
            // The end of one interval will never be equal to the start of another.
            // But the cum end of one interval is equal to cum start of another.
            // the only time we'd have an overlapping end position if if there isn't anything stored
            // and we our new interval starts at 0, because poSendPrev will be the default value of _storedPositionEnds (0)

            double newCumStart;
            if (!existingCumEnd)
                newCumStart = interval.Start * Default;
            else
                newCumStart = cumEndPrev + (interval.Start - posEndPrev - 1) * Default;
            _storedPositionStarts.Add(interval.Start);
            _storedPositionEnds.Add(interval.End);
            _cumulativeValuesAtStart.Add(newCumStart);
            _cumulativeValuesAtEnd.Add(newCumStart + intervalSize);
        }
    }

    private void ClearCumulativeData()
    {
        _storedPositionEnds.Clear();
        _storedPositionStarts.Clear();
        _cumulativeValuesAtEnd.Clear();
        _cumulativeValuesAtStart.Clear();
    }

    public override List<(int start, int end, double value)> Cut(int start, int end)
    {
        var res = base.Cut(start, end);
        UpdateCumulativePositons(start);
        return res;
    }

    public override void InsertAt(int start, int n)
    {
        base.InsertAt(start, n);
        UpdateCumulativePositons(start);
    }

    /// <summary>
    /// Returns the cumulative range size at the START of the range index.
    /// For the first range (0), it is always zero.
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    public double GetCumulative(int position)
    {
        if (!_intervals.Any())
            return position * Default;

        if (position > _intervals.End)
            return _cumulativeValuesAtEnd.Last() + (position - _storedPositionEnds.Last() - 1) * Default;

        var indexStart = _storedPositionStarts.BinarySearchIndexOf(position);
        if (indexStart >= 0)
            return _cumulativeValuesAtStart[indexStart];

        // if inside an overlapping interval, calculate the cumulative by seeing how far from the start it is
        // and the cell size in the interval
        var overlapping = this._intervals.GetOverlappingIntervals(position, position).FirstOrDefault();
        if (overlapping != null)
        {
            var startPosnIndex = _storedPositionStarts.BinarySearchIndexOf(overlapping.Start);
            // we know the above exists because its attached to an interval
            return _cumulativeValuesAtStart[startPosnIndex] + (position - overlapping.Start) * overlapping.Data.Value;
        }

        // otherwise we are to the right of zero, one or more intervals
        var closestRightStartPosition = ~indexStart;
        // we have already checked whether we are greater then the end, so this index must exist
        // plus we know we aren't overlapping any intervals.
        return _cumulativeValuesAtStart[closestRightStartPosition] -
               (_storedPositionStarts[closestRightStartPosition] - position) * Default;
    }

    public double GetSizeBetween(int start, int end)
    {
        var c0 = GetCumulative(start);
        var c1 = GetCumulative(end);
        return c1 - c0;
    }

    /// <summary>
    /// Returns the range position just BEFORE the cumulative position given.
    /// </summary>
    /// <param name="cumulative"></param>
    /// <returns></returns>
    public int GetPosition(double cumulative)
    {
        if (!_cumulativeValuesAtStart.Any())
            return (int)(cumulative / Default);

        //.....clast_start         clast_end      cum
        // .... [                       ]          x
        if (cumulative >= _cumulativeValuesAtEnd.Last())
            return _storedPositionEnds.Last() + 1 + (int)((cumulative - _cumulativeValuesAtEnd.Last()) / Default);

        var searchIndexStart = _cumulativeValuesAtStart.BinarySearchIndexOf(cumulative);
        if (searchIndexStart >= 0)
            return _storedPositionStarts[searchIndexStart];
        var searchIndexEnd = _cumulativeValuesAtEnd.BinarySearchIndexOf(cumulative);
        if (searchIndexEnd >= 0)
            return _storedPositionEnds[searchIndexEnd] + 1; // +1 because it goes into the next interval range

        searchIndexStart = ~searchIndexStart; // the next index after where it would have been found

        // if searchIndexStart = 0 then it is before the first interval and so can be calculated
        // by considering the offset from 0 and using default size
        if (searchIndexStart == 0)
            return (int)(cumulative / Default);

        if (cumulative > _cumulativeValuesAtEnd[searchIndexStart - 1])
        {
            // it is between ranges
            //          c-1end   cumulative   cstart
            // [       ],            x        [      ]
            var offset = cumulative - _cumulativeValuesAtEnd[searchIndexEnd - 1];
            return _storedPositionEnds[searchIndexStart - 1] + (int)(offset / Default);
        }

        // otherwise it must be inside an interval
        // it is between ranges
        // c-1_start   cum    c-1end           cstart
        // [            x         ],           [                   ]

        // handle the case of c_1_start = c-1end
        if (_cumulativeValuesAtStart[searchIndexStart - 1] == _cumulativeValuesAtEnd[searchIndexStart - 1])
            return _storedPositionStarts[searchIndexStart - 1];

        var oi = _intervals.Get(_storedPositionStarts[searchIndexStart - 1]);
        return _storedPositionStarts[searchIndexStart - 1] +
               (int)((cumulative - _cumulativeValuesAtStart[searchIndexStart - 1]) / Default);
    }

    public override void BatchSet(List<(int start, int end, double data)> data)
    {
        if (!data.Any())
            return;
        base.BatchSet(data);
        this.UpdateCumulativePositons(data.Select(x => x.start).Min());
    }
}