using BlazorDatasheet.Core.Commands;
using BlazorDatasheet.Core.Formats;
using BlazorDatasheet.Core.Interfaces;
using BlazorDatasheet.Core.Validation;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Store;
using BlazorDatasheet.Formula.Core;

namespace BlazorDatasheet.Core.Data.Cells;

public partial class CellStore
{
    private Sheet _sheet;

    public CellStore(Sheet sheet)
    {
        _sheet = sheet;
    }

    /// <summary>
    /// Returns all cells in the specified region
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public IEnumerable<IReadOnlyCell> GetCellsInRegion(IRegion region)
    {
        return (new BRange(_sheet, region))
            .Positions
            .Select(x => this.GetCell(x.row, x.col));
    }

    /// <summary>
    /// Returns all cells that are present in the regions given.
    /// </summary>
    /// <param name="regions"></param>
    /// <returns></returns>
    public IEnumerable<IReadOnlyCell> GetCellsInRegions(IEnumerable<IRegion> regions)
    {
        var cells = new List<IReadOnlyCell>();
        foreach (var region in regions)
            cells.AddRange(GetCellsInRegion(region));
        return cells.ToArray();
    }

    /// <summary>
    /// Returns the cell at the specified position.
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public IReadOnlyCell GetCell(int row, int col)
    {
        return new SheetCell(row, col, _sheet);
    }

    /// <summary>
    /// Returns the cell at the specified position
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    public IReadOnlyCell GetCell(CellPosition position)
    {
        return GetCell(position.row, position.col);
    }

    internal IEnumerable<CellPosition> GetNonEmptyCellPositions(IRegion region)
    {
        return _dataStore.GetNonEmptyPositions(region.TopLeft.row,
            region.BottomRight.row,
            region.TopLeft.col,
            region.BottomRight.col);
    }

    /// <summary>
    /// Clears all cell values in the region
    /// </summary>
    /// <param name="range">The range in which to clear all cells</param>
    public void ClearCells(BRange range)
    {
        var cmd = new ClearCellsCommand(range);
        _sheet.Commands.ExecuteCommand(cmd);
    }

    internal CellStoreRestoreData ClearCellsImpl(IEnumerable<IRegion> regionsToClear)
    {
        _sheet.BatchUpdates();
        var restoreData = new CellStoreRestoreData();
        var toClear = regionsToClear.ToList();
        restoreData.ValueRestoreData = _dataStore.Clear(toClear);
        restoreData.ValidRestoreData = _validStore.Clear(toClear);
        restoreData.FormulaRestoreData = ClearFormulaImpl(toClear).FormulaRestoreData;

        var affected = restoreData.GetAffectedPositions().ToList();
        EmitCellsChanged(affected);
        _sheet.MarkDirty(affected);

        _sheet.EndBatchUpdates();
        return restoreData;
    }

    /// <summary>
    /// Inserts a number of columns into each of the cell's stores.
    /// </summary>
    /// <param name="col">The column that will be replaced by the new column.</param>
    /// <param name="nCols">The number of columns to insert</param>
    /// <param name="expandNeighboring">Whether to expand any cell data to the left of the insertion. If undoing an action, best to set to false.</param>
    internal void InsertColAt(int col, int nCols, bool? expandNeighboring = null)
    {
        _dataStore.InsertColAt(col, nCols);
        _formatStore.InsertCols(col, nCols, expandNeighboring);
        _typeStore.InsertCols(col, nCols, expandNeighboring);
        _formulaStore.InsertColAt(col, nCols);
        _validStore.InsertColAt(col, nCols);
        _mergeStore.InsertCols(col, nCols, false);
    }

    /// <summary>
    /// Inserts a number of rows into each of the cell's stores.
    /// </summary>
    /// <param name="row">The row that will be replaced by the new row.</param>
    /// <param name="nRows">The number of rows to insert</param>
    /// <param name="expandNeighboring">Whether to expand any cell data to the left of the insertion. If undoing an action, best to set to false.</param>
    internal void InsertRowAt(int row, int nRows, bool? expandNeighboring = null)
    {
        _dataStore.InsertRowAt(row, nRows);
        _formatStore.InsertRows(row, nRows, expandNeighboring);
        _typeStore.InsertRows(row, nRows, expandNeighboring);
        _formulaStore.InsertRowAt(row, nRows);
        _validStore.InsertRowAt(row, nRows);
        _mergeStore.InsertRows(row, nRows, false);
    }

    internal CellStoreRestoreData RemoveRowAt(int row, int nRows)
    {
        var restoreData = new CellStoreRestoreData();
        restoreData.ValueRestoreData = _dataStore.RemoveRowAt(row, nRows);
        restoreData.ValidRestoreData = _validStore.RemoveRowAt(row, nRows);
        restoreData.TypeRestoreData = _typeStore.RemoveRows(row, row + nRows - 1);
        restoreData.FormulaRestoreData = ClearFormulaImpl(row, nRows).FormulaRestoreData;
        restoreData.FormatRestoreData = _formatStore.RemoveRows(row, row + nRows - 1);
        restoreData.MergeRestoreData = _mergeStore.RemoveRows(row, row + nRows - 1);
        _formulaStore.RemoveRowAt(row, nRows);

        return restoreData;
    }

    internal CellStoreRestoreData RemoveColAt(int col, int nCols)
    {
        var restoreData = new CellStoreRestoreData();
        restoreData.ValueRestoreData = _dataStore.RemoveColAt(col, nCols);
        restoreData.ValidRestoreData = _validStore.RemoveColAt(col, nCols);
        restoreData.TypeRestoreData = _typeStore.RemoveCols(col, col + nCols - 1);
        restoreData.FormulaRestoreData = ClearFormulaImpl(col, nCols).FormulaRestoreData;
        restoreData.FormatRestoreData = _formatStore.RemoveCols(col, col + nCols - 1);
        restoreData.MergeRestoreData = _mergeStore.RemoveCols(col, col + nCols - 1);
        _formulaStore.RemoveColAt(col, nCols);

        return restoreData;
    }

    /// <summary>
    /// Restores the internal storage state by redoing any actions that caused the internal data to change.
    /// Fires events for the changed data.
    /// </summary>
    /// <param name="restoreData"></param>
    internal void Restore(CellStoreRestoreData restoreData)
    {
        _sheet.BatchUpdates();
        // Set formula through this function so we add the formula back in to the dependency graph
        foreach (var data in restoreData.FormulaRestoreData.DataRemoved)
            this.SetFormulaImpl(data.row, data.col, data.data);

        _validStore.Restore(restoreData.ValidRestoreData);
        _typeStore.Restore(restoreData.TypeRestoreData);
        _dataStore.Restore(restoreData.ValueRestoreData);
        _formatStore.Restore(restoreData.FormatRestoreData);
        _mergeStore.Restore(restoreData.MergeRestoreData);

        foreach (var pt in restoreData.ValueRestoreData.DataRemoved)
        {
            _sheet.MarkDirty(pt.row, pt.col);
            EmitCellChanged(pt.row, pt.col);
        }

        foreach (var region in restoreData.GetAffectedRegions())
        {
            _sheet.MarkDirty(region);
        }

        _sheet.EndBatchUpdates();
    }


    /// <summary>
    /// The <see cref="SheetCell"/> at position row, col.
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    public SheetCell this[int row, int col]
    {
        get { return new SheetCell(row, col, _sheet); }
    }
}