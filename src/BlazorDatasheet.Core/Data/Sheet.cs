using System.Diagnostics;
using System.Text;
using BlazorDatasheet.Core.Commands;
using BlazorDatasheet.Core.Data.Cells;
using BlazorDatasheet.Core.Edit;
using BlazorDatasheet.Core.Events.Visual;
using BlazorDatasheet.Core.Formats;
using BlazorDatasheet.Core.Interfaces;
using BlazorDatasheet.Core.Selecting;
using BlazorDatasheet.Core.Validation;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.Formula.Core;
using BlazorDatasheet.Formula.Core.Interpreter.References;

namespace BlazorDatasheet.Core.Data;

public class Sheet
{
    /// <summary>
    /// The total number of rows in the sheet
    /// </summary>
    public int NumRows { get; private set; }

    /// <summary>
    /// The total of columns in the sheet
    /// </summary>
    public int NumCols { get; private set; }

    /// <summary>
    /// Start/finish edits.
    /// </summary>
    public Editor Editor { get; }

    /// <summary>
    /// Interact with cells & cell data.
    /// </summary>
    public CellStore Cells { get; }

    /// <summary>
    /// Managers commands & undo/redo. Default is true.
    /// </summary>
    public CommandManager Commands { get; }

    /// <summary>
    /// Manages sheet formula
    /// </summary>
    public FormulaEngine.FormulaEngine FormulaEngine { get; }

    /// <summary>
    /// The bounds of the sheet
    /// </summary>
    public Region Region => new Region(0, NumRows - 1, 0, NumCols - 1);

    /// <summary>
    /// Provides functions for managing the sheet's conditional formatting
    /// </summary>
    public ConditionalFormatManager ConditionalFormats { get; }

    /// <summary>
    /// Manages and holds information on cell validators.
    /// </summary>
    public ValidationManager Validators { get; }

    /// <summary>
    /// Contains data, including width, on each column.
    /// </summary>
    public ColumnInfoStore Columns { get; private set; }

    /// <summary>
    /// Contains data, including height, on each row.
    /// </summary>
    public RowInfoStore Rows { get; private set; }

    /// <summary>
    /// The sheet's active selection
    /// </summary>
    public Selection Selection { get; }

    internal IDialogService? Dialog { get; private set; }

    public IInputService InputService { get; private set; }

    #region EVENTS

    /// <summary>
    /// Fired when a portion of the sheet is marked as dirty.
    /// </summary>
    public event EventHandler<DirtySheetEventArgs>? SheetDirty;

    #endregion

    /// <summary>
    /// True if the sheet is not firing dirty events until <see cref="EndBatchUpdates"/> is called.
    /// </summary>
    private bool _isBatchingChanges;

    /// <summary>
    /// If the sheet is batching dirty regions, they are stored here.
    /// </summary>
    private List<IRegion> _dirtyRegions = new();

    /// <summary>
    /// If the sheet is batching dirty cells, they are stored here.
    /// </summary>
    private HashSet<CellPosition> _dirtyPositions = new();

    private Sheet()
    {
        Cells = new Cells.CellStore(this);
        Commands = new CommandManager(this);
        Selection = new Selection(this);
        Editor = new Editor(this);
        Validators = new ValidationManager(this);
        Rows = new RowInfoStore(25, this);
        Columns = new ColumnInfoStore(105, this);
        FormulaEngine = new FormulaEngine.FormulaEngine(this);
        ConditionalFormats = new ConditionalFormatManager(this, Cells);
    }

    public Sheet(int numRows, int numCols) : this()
    {
        NumCols = numCols;
        NumRows = numRows;
    }


    #region COLS

    internal void AddCols(int nCols = 1)
    {
        NumCols += nCols;
    }

    internal void RemoveCols(int nCols = 1)
    {
        NumCols -= nCols;
    }

    #endregion

    #region ROWS

    internal void AddRows(int nRows = 1)
    {
        NumRows += nRows;
    }

    internal void RemoveRows(int nRows)
    {
        NumRows -= nRows;
    }

    #endregion

    /// <summary>
    /// Returns a single cell range at the position row, col
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public SheetRange Range(int row, int col)
    {
        return new SheetRange(this, row, col);
    }

    /// <summary>
    /// Returns a range with the positions specified
    /// </summary>
    /// <param name="rowStart"></param>
    /// <param name="rowEnd"></param>
    /// <param name="colStart"></param>
    /// <param name="colEnd"></param>
    /// <returns></returns>
    public SheetRange Range(int rowStart, int rowEnd, int colStart, int colEnd)
    {
        return Range(new Region(rowStart, rowEnd, colStart, colEnd));
    }

    /// <summary>
    /// Returns a new range that contains the region specified
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public SheetRange Range(IRegion region)
    {
        return new SheetRange(this, region);
    }

    /// <summary>
    /// The <see cref="SheetRange"/> specified by the string e.g A1, B1:B4, A:B, A:A, 2:4, etc.
    /// Multiple regions can be included by separating them with a ","
    /// </summary>
    public SheetRange? Range(string rangeStr)
    {
        if (string.IsNullOrEmpty(rangeStr))
            return null;

        var rangeStrFormula = $"={rangeStr}";
        var evaluatedValue =
            FormulaEngine.Evaluate(FormulaEngine.ParseFormula(rangeStrFormula), resolveReferences: false);
        if (evaluatedValue.ValueType == CellValueType.Reference)
        {
            var reference = evaluatedValue.GetValue<Reference>();
            return Range(reference!.ToRegion());
        }

        return null;
    }

    /// <summary>
    /// Returns a column or row range, depending on the axis provided
    /// </summary>
    /// <param name="axis">The axis of the range (row or column)</param>
    /// <param name="start">The start row/column index</param>
    /// <param name="end">The end row/column index</param>
    /// <returns></returns>
    public SheetRange? Range(Axis axis, int start, int end)
    {
        switch (axis)
        {
            case Axis.Col:
                return Range(new ColumnRegion(start, end));
            case Axis.Row:
                return Range(new RowRegion(start, end));
        }

        return null;
    }

    /// <summary>
    /// Mark the cells specified by positions dirty.
    /// </summary>
    /// <param name="positions"></param>
    internal void MarkDirty(IEnumerable<CellPosition> positions)
    {
        if (_isBatchingChanges)
        {
            foreach (var position in positions)
                _dirtyPositions.Add(position);
        }
        else
            SheetDirty?.Invoke(this, new DirtySheetEventArgs()
            {
                DirtyPositions = positions.ToHashSet()
            });
    }

    /// <summary>
    /// Marks the cell as dirty and requiring re-render
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    internal void MarkDirty(int row, int col)
    {
        if (_isBatchingChanges)
            _dirtyPositions.Add(new CellPosition(row, col));
        else
            SheetDirty?.Invoke(this, new DirtySheetEventArgs()
            {
                DirtyPositions = new HashSet<CellPosition>() { new CellPosition(row, col) }
            });
    }

    /// <summary>
    /// Marks the region as dirty and requiring re-render.
    /// </summary>
    /// <param name="region"></param>
    internal void MarkDirty(IRegion region)
    {
        MarkDirty(new List<IRegion>() { region });
    }

    /// <summary>
    /// Marks the regions as dirty and requiring re-render.
    /// </summary>
    /// <param name="regions"></param>
    internal void MarkDirty(IEnumerable<IRegion> regions)
    {
        if (_isBatchingChanges)
            _dirtyRegions.AddRange(regions);
        else
            SheetDirty?.Invoke(
                this, new DirtySheetEventArgs() { DirtyRegions = regions, DirtyPositions = _dirtyPositions });
    }

    private int _batchRequestNo = 0;

    /// <summary>
    /// Batches dirty cell and region additions, as well as cell value changes to emit events once rather
    /// than every time a cell is dirty or a value is changed.
    /// <returns>Returns false if the sheet was already batching, true otherwise.</returns>
    /// </summary>
    public void BatchUpdates()
    {
        _batchRequestNo++;

        if (_isBatchingChanges)
            return;

        _dirtyPositions.Clear();
        _dirtyRegions.Clear();
        Cells.BatchChanges();
        _isBatchingChanges = true;
    }

    /// <summary>
    /// Ends the batching of dirty cells and regions, and emits the dirty sheet event.
    /// </summary>
    public void EndBatchUpdates()
    {
        _batchRequestNo--;

        if (_batchRequestNo > 0)
            return;

        Cells.EndBatchChanges();

        // Checks for batching changes here, because the cells changed event
        // may start batching more dirty changes again from subscribers of that event.
        if (_dirtyRegions.Any() || _dirtyPositions.Any() && _isBatchingChanges)
        {
            SheetDirty?.Invoke(this, new DirtySheetEventArgs()
            {
                DirtyRegions = _dirtyRegions,
                DirtyPositions = _dirtyPositions
            });
        }

        _isBatchingChanges = false;
    }

    /// <summary>
    /// Inserts delimited text from the given position
    /// And assigns cell's values based on the delimited text (tabs and newlines)
    /// Returns the region of cells that surrounds all cells that are affected
    /// </summary>
    /// <param name="text">The text to insert</param>
    /// <param name="inputPosition">The position where the insertion starts</param>
    /// <param name="newLineDelim">The delimiter that specifies a new-line</param>
    /// <returns>The region of cells that were affected</returns>
    public Region? InsertDelimitedText(string text, CellPosition inputPosition)
    {
        if (!Region.Contains(inputPosition))
            return null;

        ReadOnlySpan<string> lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines[^1] is [])
            lines = lines[..^1];

        // We may reach the end of the sheet, so we only need to paste the rows up until the end.
        var endRow = Math.Min(inputPosition.row + lines.Length - 1, NumRows - 1);
        // Keep track of the maximum end column that we are inserting into
        // This is used to determine the region to return.
        // It is possible that each line is of different cell lengths, so we return the max for all lines
        var maxEndCol = -1;

        var valChanges = new List<(int row, int col, object value)>();

        int lineNo = 0;
        for (int row = inputPosition.row; row <= endRow; row++)
        {
            var lineSplit = lines[lineNo].Split('\t');
            // Same thing as above with the number of columns
            var endCol = Math.Min(inputPosition.col + lineSplit.Length - 1, NumCols - 1);

            maxEndCol = Math.Max(endCol, maxEndCol);

            int cellIndex = 0;
            for (int col = inputPosition.col; col <= endCol; col++)
            {
                valChanges.Add((row, col, lineSplit[cellIndex]));
                cellIndex++;
            }

            lineNo++;
        }

        Cells.SetValues(valChanges);

        return new Region(inputPosition.row, endRow, inputPosition.col, maxEndCol);
    }

    #region FORMAT

    /// <summary>
    /// Returns the format that is visible at the cell position row, col.
    /// The order to determine which format is visible is
    /// 1. Cell format (if it exists)
    /// 2. Column format
    /// 3. Row format
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public CellFormat? GetFormat(int row, int col)
    {
        var defaultFormat = new CellFormat();
        var cellFormat = Cells.GetFormat(row, col).Clone();
        var rowFormat = Rows.RowFormats.Get(row)?.Clone() ?? defaultFormat;
        var colFormat = Columns.ColFormats.Get(col)?.Clone() ?? defaultFormat;

        rowFormat.Merge(colFormat);
        rowFormat.Merge(cellFormat);
        return rowFormat;
    }

    /// <summary>
    /// Sets the format for a particular range
    /// </summary>
    /// <param name="range"></param>
    /// <param name="cellFormat"></param>
    public void SetFormat(IRegion region, CellFormat cellFormat)
    {
        var cmd = new SetFormatCommand(region, cellFormat);
        Commands.ExecuteCommand(cmd);
    }

    public void SetFormat(IEnumerable<IRegion> regions, CellFormat cellFormat)
    {
        Commands.BeginCommandGroup();
        foreach (var region in regions)
        {
            Commands.ExecuteCommand(new SetFormatCommand(region, cellFormat));
        }

        Commands.EndCommandGroup();
    }

    #endregion

    public string? GetRegionAsDelimitedText(IRegion inputRegion, char tabDelimiter = '\t', string newLineDelim = "\n")
    {
        if (inputRegion.Area == 0)
            return string.Empty;

        var intersection = inputRegion.GetIntersection(this.Region);
        if (intersection == null)
            return null;

        var range = intersection.Copy();

        var strBuilder = new StringBuilder();

        var r0 = range.TopLeft.row;
        var r1 = range.BottomRight.row;
        var c0 = range.TopLeft.col;
        var c1 = range.BottomRight.col;

        for (int row = r0; row <= r1; row++)
        {
            for (int col = c0; col <= c1; col++)
            {
                var cell = Cells.GetCell(row, col);
                var value = cell.Value;
                if (value == null)
                    strBuilder.Append("");
                else
                {
                    if (value is string s)
                    {
                        strBuilder.Append(s.Replace(newLineDelim, " ").Replace(tabDelimiter, ' '));
                    }
                    else
                    {
                        strBuilder.Append(value);
                    }
                }

                if (col != c1)
                    strBuilder.Append(tabDelimiter);
            }

            strBuilder.Append(newLineDelim);
        }

        return strBuilder.ToString();
    }

    public void SetDialogService(IDialogService? service)
    {
        Dialog = service;
    }

    public void SetInputService(IInputService service)
    {
        InputService = service;
    }
}