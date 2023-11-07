﻿using BlazorDatasheet.Core.Data;
using BlazorDatasheet.Core.Data.Cells;
using BlazorDatasheet.Core.Events;
using BlazorDatasheet.Core.Events.Layout;
using BlazorDatasheet.Core.Interfaces;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.RTree;
using BlazorDatasheet.DataStructures.Store;
using BlazorDatasheet.DataStructures.Util;

namespace BlazorDatasheet.Core.Formats;

public class ConditionalFormatManager
{
    private readonly Sheet _sheet;

    private readonly List<ConditionalFormatAbstractBase> _registered = new();
    private readonly ConsolidatedDataStore<ConditionalFormatAbstractBase> _appliedFormats = new();

    public ConditionalFormatManager(Sheet sheet,
        CellStore cellStore)
    {
        _sheet = sheet;
        cellStore.CellsChanged += HandleCellsChanged;
    }

    /// <summary>
    /// Applies the conditional format specified by "key" to all cells in a region
    /// </summary>
    /// <param name="region"></param>
    /// <param name="conditionalFormat"></param>
    public void Apply(IRegion? region, ConditionalFormatAbstractBase conditionalFormat)
    {
        Apply(new SheetRange(_sheet, region), conditionalFormat);
    }

    /// <summary>
    /// Applies the conditional format to the region
    /// </summary>
    /// <param name="range"></param>
    /// <param name="conditionalFormat"></param>
    public void Apply(SheetRange? range, ConditionalFormatAbstractBase conditionalFormat)
    {
        if (range == null)
            return;

        if (!_registered.Contains(conditionalFormat))
        {
            _registered.Add(conditionalFormat);
            conditionalFormat.Order = _registered.Count - 1;
        }

        _appliedFormats.Add(range.Region, conditionalFormat);
        Prepare(new List<ConditionalFormatAbstractBase>() { conditionalFormat });
    }

    private List<SheetRange> GetRangesAppliedToFormat(ConditionalFormatAbstractBase format)
    {
        var regions = _appliedFormats.GetRegions(format);
        return regions.Select(x => new SheetRange(_sheet, x)).ToList();
    }


    private void HandleCellsChanged(object? sender, IEnumerable<CellPosition> args)
    {
        // Simply prepare all cells that the conditional format belongs to (if shared)
        var cfs =
            args.SelectMany(x => GetFormatsAppliedToPosition(x.row, x.col))
                .Distinct()
                .ToList();
        Prepare(cfs);
    }

    private void Prepare(List<ConditionalFormatAbstractBase> formats)
    {
        foreach (var format in formats)
        {
            // prepare format (re-compute shared format cache etch.)
            if (format.IsShared)
            {
                format.Prepare(GetRangesAppliedToFormat(format));
                _sheet.MarkDirty(GetRangesAppliedToFormat(format).Select(x => x.Region));
            }
        }
    }

    private IEnumerable<ConditionalFormatAbstractBase> GetFormatsAppliedToPosition(int row, int col)
    {
        var region = new Region(row, col);
        return _appliedFormats.GetData(row, col);
    }

    /// <summary>
    /// Applies the conditional format specified by "key" to a particular cell. If setting the format to a number of cells,
    /// prefer setting via a region.
    /// <param name="format"></param>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// </summary>
    public void Apply(int row, int col, ConditionalFormatAbstractBase format)
    {
        Apply(new Region(row, col), format);
    }

    /// <summary>
    /// Returns the format that results from applying all conditional formats to this cell
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public CellFormat? GetFormatResult(int row, int col)
    {
        if (!_sheet.Region.Contains(row, col))
            return null;
        var cfs = GetFormatsAppliedToPosition(row, col);
        CellFormat? initialFormat = null;
        foreach (var format in cfs)
        {
            var apply = format.Predicate?.Invoke(new CellPosition(row, col), _sheet);
            if (apply == false)
                continue;
            var calced = format.CalculateFormat(row, col, _sheet);
            if (initialFormat == null)
                initialFormat = calced;
            else
                initialFormat.Merge(calced);
            if (apply == true && format.StopIfTrue)
                break;
        }

        return initialFormat;
    }

    internal void InsertRowAt(int row, int nRows, bool expandNeighbouring = false)
    {
        _appliedFormats.InsertRows(row, nRows, expandNeighbouring);
    }

    internal void InsertColAt(int row, int nRows, bool expandNeighbouring = false)
    {
        _appliedFormats.InsertCols(row, nRows, expandNeighbouring);
    }

    internal RegionRestoreData<ConditionalFormatAbstractBase> RemoveRowAt(int row, int nRows)
    {
        var restoreData = _appliedFormats.RemoveRows(row, row + nRows - 1);
        var cfsAffected = restoreData.RegionsAdded
            .Select(x => x.Data).Concat(restoreData.RegionsRemoved.Select(x => x.Data))
            .Distinct()
            .ToList();
        Prepare(cfsAffected);
        return restoreData;
    }

    internal RegionRestoreData<ConditionalFormatAbstractBase> RemoveColAt(int col, int nCols)
    {
        var restoreData = _appliedFormats.RemoveCols(col, col + nCols - 1);
        var cfsAffected = restoreData.RegionsAdded
            .Select(x => x.Data).Concat(restoreData.RegionsRemoved.Select(x => x.Data))
            .Distinct()
            .ToList();
        Prepare(cfsAffected);
        return restoreData;
    }

    internal void Restore(RegionRestoreData<ConditionalFormatAbstractBase> data)
    {
        _appliedFormats.Restore(data);
        var cfsAffected = data.RegionsAdded
            .Select(x => x.Data).Concat(data.RegionsRemoved.Select(x => x.Data))
            .Distinct()
            .ToList();
        Prepare(cfsAffected);
    }
}