using BlazorDatasheet.DataStructures.Geometry;

namespace BlazorDatasheet.DataStructures.Store;

public class SparseMatrixStoreByRows<T> : IMatrixDataStore<T>
{
    private SparseList<SparseList<T>> _rows;
    private readonly T? _defaultIfEmpty;
    private readonly SparseList<T> _emptyRow;

    public SparseMatrixStoreByRows(T defaultIfEmpty)
    {
        _defaultIfEmpty = defaultIfEmpty;
        _emptyRow = new SparseList<T>(defaultIfEmpty);
        _rows = new SparseList<SparseList<T>>(_emptyRow);
    }

    public SparseMatrixStoreByRows() : this(default(T))
    {
    }

    public bool Contains(int row, int col)
    {
        return _rows.Get(row).ContainsIndex(col);
    }

    public T? Get(int row, int col)
    {
        return _rows.Get(row)
            .Get(col);
    }

    public MatrixRestoreData<T> Set(int row, int col, T value)
    {
        var restoreData = new MatrixRestoreData<T>();
        SparseList<T> rowList;
        if (!_rows.ContainsIndex(row))
        {
            rowList = new SparseList<T>(_defaultIfEmpty);
            _rows.Set(row, rowList);
        }
        else
        {
            rowList = _rows.Get(row);
        }

        var currValue = rowList.Get(col);
        rowList.Set(col, value);
        restoreData.DataRemoved = new() { (row, col, currValue) };
        return restoreData;
    }

    public MatrixRestoreData<T> Clear(int row, int col)
    {
        var rowExists = _rows.ContainsIndex(row);
        if (!rowExists)
            return new MatrixRestoreData<T>();

        var rowList = _rows.Get(row);
        var restoreData = rowList.Clear(col);
        if (restoreData == null)
            return new MatrixRestoreData<T>();

        return new MatrixRestoreData<T>()
        {
            DataRemoved = new() { (row, col, restoreData.Value.value) }
        };
    }

    public MatrixRestoreData<T> Clear(IEnumerable<CellPosition> positions)
    {
        var cleared = positions.Select(x => Clear(x.row, x.col));
        var clearedData = cleared.SelectMany(x => x.DataRemoved).ToList();
        return new MatrixRestoreData<T>()
        {
            DataRemoved = clearedData
        };
    }

    public MatrixRestoreData<T> Clear(IRegion region)
    {
        var cleared = new List<(int row, int col, T)>();
        var nonEmptyRows = _rows.GetNonEmptyDataBetween(region.Top, region.Bottom);
        foreach (var row in nonEmptyRows)
        {
            var clearedRowData = row.data.ClearBetween(region.Left, region.Right);
            cleared.AddRange(clearedRowData.Select(x =>
                (row.itemIndex, x.itemIndexCleared, x.Item2)
            ));
        }

        return new MatrixRestoreData<T>()
        {
            DataRemoved = cleared!
        };
    }

    public MatrixRestoreData<T> Clear(IEnumerable<IRegion> regions)
    {
        var cleared = regions.Select(Clear).SelectMany(x => x.DataRemoved);
        return new MatrixRestoreData<T>()
        {
            DataRemoved = cleared.ToList()
        };
    }

    public void InsertRowAt(int row, int nRows)
    {
        _rows.InsertAt(row, nRows);
    }

    public void InsertColAt(int col, int nCols)
    {
        foreach (var row in _rows.Values)
            row.Value.InsertAt(col, nCols);
    }

    public MatrixRestoreData<T> RemoveColAt(int col, int nCols)
    {
        var removed = new List<(int row, int col, T data)>();
        foreach (var row in _rows.Values)
        {
            var removedColData = row.Value.DeleteAt(col, nCols);
            removed.AddRange(removedColData.Select(x =>
                (row.Key, x.indexDeleted, x.value)
            ));
        }

        return new MatrixRestoreData<T>()
        {
            DataRemoved = removed!
        };
    }

    public int GetNextNonBlankRow(int row, int col)
    {
        var rowIndex = _rows.GetNextNonEmptyItemKey(row + 1);
        while (rowIndex != -1)
        {
            if (_rows.Get(rowIndex).ContainsIndex(col))
                return rowIndex;
            rowIndex = _rows.GetNextNonEmptyItemKey(rowIndex + 1);
        }

        return -1;
    }

    public int GetNextNonBlankColumn(int row, int col)
    {
        if (!_rows.ContainsIndex(row))
            return -1;

        return _rows.Get(row).GetNextNonEmptyItemKey(col + 1);
    }

    public MatrixRestoreData<T> RemoveRowAt(int row, int nRows)
    {
        var removedRows = _rows.DeleteAt(row, nRows);
        var removedData = new List<(int row, int col, T data)>();
        foreach (var removedRow in removedRows)
        {
            var nonEmptyColData = removedRow.value.GetNonEmptyData();
            removedData.AddRange(nonEmptyColData.Select(x =>
                (removedRow.indexDeleted, x.itemIndex, x.data)
            ));
        }

        return new MatrixRestoreData<T>()
        {
            DataRemoved = removedData!
        };
    }

    public IEnumerable<CellPosition> GetNonEmptyPositions(int r0, int r1, int c0, int c1)
    {
        var posns = new System.Collections.Generic.List<CellPosition>();
        var nonEmptyRows = _rows.GetNonEmptyDataBetween(r0, r1);
        foreach (var row in nonEmptyRows)
        {
            foreach (var col in row.data.GetNonEmptyDataBetween(c0, c1))
                posns.Add(new CellPosition(row.itemIndex, col.itemIndex));
        }

        return posns;
    }

    public MatrixRestoreData<T> Copy(IRegion fromRegion, IRegion toRegion)
    {
        var nonEmptyCopyData = this.GetNonEmptyData(fromRegion).ToList();
        var restoreData = this.Clear(toRegion);

        var dr = toRegion.Top - fromRegion.Top;
        var dc = toRegion.Left - fromRegion.Left;

        foreach (var v in nonEmptyCopyData)
            this.Set(v.row + dr, v.col + dc, v.data);

        return restoreData;
    }

    public void Restore(MatrixRestoreData<T> restoreData)
    {
        foreach (var pt in restoreData.DataRemoved)
            Set(pt.row, pt.col, pt.data);
    }

    public IEnumerable<(int row, int col, T data)> GetNonEmptyData(IRegion region)
    {
        var data = new System.Collections.Generic.List<(int row, int col, T data)>();
        var nonEmptyRows = _rows.GetNonEmptyDataBetween(region.Top, region.Bottom);
        foreach (var row in nonEmptyRows)
        {
            foreach (var col in row.data.GetNonEmptyDataBetween(region.Left, region.Right))
                data.Add((row.itemIndex, col.itemIndex, col.data));
        }

        return data;
    }

    public T[][] GetData(IRegion region)
    {
        var res = new T[region.Height][];
        for (int i = 0; i < region.Height; i++)
        {
            var rowData = _rows.Get(i + region.Top);
            res[i] = rowData.GetDataBetween(region.Left, region.Right);
        }

        return res;
    }

    public IMatrixDataStore<T> GetSubMatrix(IRegion region, bool newStoreResetsOffsets = true)
    {
        var store = new SparseMatrixStoreByRows<T>(_defaultIfEmpty);
        int r0 = region.Top;
        int r1 = region.Bottom;
        int c0 = region.Left;
        int c1 = region.Right;

        int rowOffset = newStoreResetsOffsets ? r0 : 0;
        int colOffset = newStoreResetsOffsets ? c0 : 0;

        var nonEmptyRows = _rows.GetNonEmptyDataBetween(r0, r1);
        foreach (var row in nonEmptyRows)
        {
            var data = row.data.GetNonEmptyDataBetween(c0, c1);
            foreach (var col in data)
                store.Set(row.itemIndex - rowOffset, col.itemIndex - colOffset, col.data);
        }

        return store;
    }

    public RowDataCollection<T> GetNonEmptyRowData(IRegion region)
    {
        var nonEmptyRows = _rows.GetNonEmptyDataBetween(region.Top, region.Bottom);
        var rowIndices = new int[nonEmptyRows.Count];
        var rowDataArray = new RowData<T>[nonEmptyRows.Count];
        for (int i = 0; i < nonEmptyRows.Count; i++)
        {
            var row = nonEmptyRows[i];
            rowIndices[i] = row.itemIndex;
            var nonEmptyCols = row.data.GetNonEmptyDataBetween(region.Left, region.Right);
            var colIndices = nonEmptyCols.Select(x => x.itemIndex).ToArray();
            var colData = nonEmptyCols.Select(x => x.data).ToArray();
            var rowData = new RowData<T>(colIndices, colData);
            rowDataArray[i] = rowData;
        }

        return new RowDataCollection<T>(rowIndices, rowDataArray);
    }
}