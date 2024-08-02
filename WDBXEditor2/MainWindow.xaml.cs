using DBCD;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WDBXEditor2.Controller;
using WDBXEditor2.Misc;

namespace WDBXEditor2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DBLoader dbLoader = new DBLoader();
        private string currentOpenDB2 = string.Empty;
        private IDBCDStorage openedDB2Storage;

        public MainWindow()
        {
            InitializeComponent();
            SettingStorage.Initialize();

            Exit.Click += (e, o) => Close();

            Title = $"WDBXEditor2  -  {Constants.Version}";
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "DB2 Files (*.db2)|*.db2",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var files = openFileDialog.FileNames;

                foreach (string loadedDBpath in dbLoader.LoadFiles(files))
                    OpenDBItems.Items.Add(loadedDBpath);
            }
        }

        private void OpenDBItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Clear DataGrid
            DB2DataGrid.Columns.Clear();
            DB2DataGrid.ItemsSource = new List<string>();

            DB2InfoDataGrid.Columns.Clear();
            DB2InfoDataGrid.ItemsSource = new List<string>();


            currentOpenDB2 = (string)OpenDBItems.SelectedItem;
            if (currentOpenDB2 == null)
                return;

            if (dbLoader.LoadedDBFiles.TryGetValue(currentOpenDB2, out IDBCDStorage storage))
            {
                var stopWatch = new Stopwatch();
                stopWatch.Start();

                var data = new DataTable();
                PopulateColumns(storage, ref data);
                if (storage.Values.Count > 0)
                    PopulateDataView(storage, ref data);

                stopWatch.Stop();
                Console.WriteLine($"Populating Grid: {currentOpenDB2} Elapsed Time: {stopWatch.Elapsed}");

                openedDB2Storage = storage;
                DB2DataGrid.ItemsSource = data.DefaultView;

                //info data
                var datainfo = new DataTable();
                DBFileReaderLib.DBParser dp = storage.parser;
                datainfo.Columns.Add("Field");
                datainfo.Columns.Add("Data");
                datainfo.Columns.Add("Comments");

                string strComment = "db2 header";
                datainfo.Rows.Add("RecordsCount", dp.RecordsCount, strComment);
                datainfo.Rows.Add("FieldsCount", dp.FieldsCount, strComment);
                datainfo.Rows.Add("RecordSize", dp.RecordSize, strComment);
                datainfo.Rows.Add("StringTableSize", dp.StringTableSize, strComment);
                datainfo.Rows.Add("TableHash", dp.TableHash, strComment);
                datainfo.Rows.Add("LayoutHash", dp.LayoutHash, strComment);
                datainfo.Rows.Add("min_id", dp.min_id, strComment);
                datainfo.Rows.Add("max_id", dp.max_id, strComment);
                datainfo.Rows.Add("local", dp.local, strComment);
                datainfo.Rows.Add("Flags", (int)dp.Flags, strComment);
                datainfo.Rows.Add("IdFieldIndex", dp.IdFieldIndex, strComment);
                datainfo.Rows.Add("total_field_count", dp.FieldsCount, strComment);
                datainfo.Rows.Add("bitpacked_data_offset", dp.bitpacked_data_offset, strComment);
                datainfo.Rows.Add("lookupColumnCount", dp.lookupColumnCount, strComment);
                datainfo.Rows.Add("field_info_size", dp.field_info_size, strComment);
                datainfo.Rows.Add("commonDataSize", dp.commonDataSize, strComment);
                datainfo.Rows.Add("palletDataSize", dp.palletDataSize, strComment);
                datainfo.Rows.Add("SectionsCount", dp.SectionsCount, strComment);

                //section info
                for (int i = 0; i < dp.SectionHeaders.Count; i++)
                {
                    strComment = String.Format("Section{0} header", i);
                    datainfo.Rows.Add("tact_key_hash", dp.SectionHeaders[i].TactKeyLookup, strComment);
                    datainfo.Rows.Add("FileOffset", dp.SectionHeaders[i].FileOffset, strComment);
                    datainfo.Rows.Add("NumRecords", dp.SectionHeaders[i].NumRecords, strComment);
                    datainfo.Rows.Add("StringTableSize", dp.SectionHeaders[i].StringTableSize, strComment);
                    datainfo.Rows.Add("OffsetRecordsEndOffset", dp.SectionHeaders[i].OffsetRecordsEndOffset, strComment);
                    datainfo.Rows.Add("IndexDataSize", dp.SectionHeaders[i].IndexDataSize, strComment);
                    datainfo.Rows.Add("ParentLookupDataSize", dp.SectionHeaders[i].ParentLookupDataSize, strComment);
                    datainfo.Rows.Add("OffsetMapIDCount", dp.SectionHeaders[i].OffsetMapIDCount, strComment);
                    datainfo.Rows.Add("CopyTableCount", dp.SectionHeaders[i].CopyTableCount, strComment);
                }

                DB2InfoDataGrid.ItemsSource = datainfo.DefaultView;

            }

            Title = $"WDBXEditor2  -  {Constants.Version}  -  {currentOpenDB2}";
        }

        /// <summary>
        /// Populate the DataView with the DB2 Columns.
        /// </summary>
        private void PopulateColumns(IDBCDStorage storage, ref DataTable data)
        {
 
            if (storage.Values.Count == 0)
            {
                data.Columns.Add("No data");
                return;
            }

            var firstItem = storage.Values.First();

            foreach (string columnName in firstItem.GetDynamicMemberNames())
            {
                var columnValue = firstItem[columnName];

                if (columnValue.GetType().IsArray)
                {
                    Array columnValueArray = (Array)columnValue;
                    for (var i = 0; i < columnValueArray.Length; ++i)
                        data.Columns.Add(columnName + i);
                }
                else
                    data.Columns.Add(columnName);
            }
        }

        /// <summary>
        /// Populate the DataView with the DB2 Data.
        /// </summary>
        private void PopulateDataView(IDBCDStorage storage, ref DataTable data)
        {
            foreach (var rowData in storage.Values)
            {
                var row = data.NewRow();

                foreach (string columnName in rowData.GetDynamicMemberNames())
                {
                    var columnValue = rowData[columnName];

                    if (columnValue.GetType().IsArray)
                    {
                        Array columnValueArray = (Array)columnValue;
                        for (var i = 0; i < columnValueArray.Length; ++i)
                            row[columnName + i] = columnValueArray.GetValue(i);
                    }
                    else
                        row[columnName] = columnValue;
                }

                data.Rows.Add(row);
            }
        }

        /// <summary>
        /// Close the currently opened DB2 file.
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Title = $"WDBXEditor2  -  {Constants.Version}";

            // Remove the DB2 file from the open files.
            OpenDBItems.Items.Remove(currentOpenDB2);

            // Clear DataGrid
            DB2DataGrid.Columns.Clear();

            currentOpenDB2 = string.Empty;
            openedDB2Storage = null;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(currentOpenDB2))
                dbLoader.LoadedDBFiles[currentOpenDB2].Save(currentOpenDB2);
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentOpenDB2))
                return;

            var saveFileDialog = new SaveFileDialog
            {
                FileName = currentOpenDB2,
                Filter = "DB2 Files (*.db2)|*.db2",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer)
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                dbLoader.LoadedDBFiles[currentOpenDB2].Save(saveFileDialog.FileName);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void DB2DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (e.Column != null)
                {
                    var rowIdx = e.Row.GetIndex();

                    //should be -1 if not allow to edit
                    if (rowIdx > openedDB2Storage.Keys.Count)
                        throw new Exception();

                    var newVal = e.EditingElement as TextBox;

                    //var dbcRow = openedDB2Storage.Values.ElementAt(rowIdx);
                    //dbcRow[currentOpenDB2, e.Column.Header.ToString()] = newVal.Text;

                    //
                    if (rowIdx == openedDB2Storage.Keys.Count)
                    {
                        //new row
                        var dbcRow = openedDB2Storage.Values.ElementAt(rowIdx - 1);
                        openedDB2Storage.Add(openedDB2Storage.Keys.Last() + 1, dbcRow);
                        //modify last row
                        dbcRow = openedDB2Storage.Values.ElementAt(rowIdx);
                        dbcRow[currentOpenDB2, e.Column.Header.ToString()] = newVal.Text;
                    }
                    else
                    {
                        var dbcRow = openedDB2Storage.Values.ElementAt(rowIdx);
                        dbcRow[currentOpenDB2, e.Column.Header.ToString()] = newVal.Text;
                    }


                    Console.WriteLine($"RowIdx: {rowIdx} Text: {newVal.Text}");
                }
            }
        }

        private void ExportData_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentOpenDB2))
                return;

            var saveFileDialog = new SaveFileDialog
            {
                FileName = currentOpenDB2,
                Filter = "csv Files (*.csv)|*.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer)
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                DB2DataGrid.SelectAllCells();

                DB2DataGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                System.Windows.Input.ApplicationCommands.Copy.Execute(null, DB2DataGrid);

                DB2DataGrid.UnselectAllCells();

                string result = (string)System.Windows.Clipboard.GetData(System.Windows.DataFormats.CommaSeparatedValue);

                File.WriteAllText(saveFileDialog.FileName, result, UnicodeEncoding.UTF8);
            }

        }
        private void ExportInfo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentOpenDB2))
                return;

            var saveFileDialog = new SaveFileDialog
            {
                FileName = currentOpenDB2 + ".info" ,
                Filter = "csv Files (*.csv)|*.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer)
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                DB2InfoDataGrid.SelectAllCells();

                DB2InfoDataGrid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                System.Windows.Input.ApplicationCommands.Copy.Execute(null, DB2InfoDataGrid);

                DB2InfoDataGrid.UnselectAllCells();

                string result = (string)System.Windows.Clipboard.GetData(System.Windows.DataFormats.CommaSeparatedValue);

                File.WriteAllText(saveFileDialog.FileName, result, UnicodeEncoding.UTF8);
            }

        }
    }
}
