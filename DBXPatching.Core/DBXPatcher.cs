using DBCD;
using DBDefsLib;
using DBXPatching.Core.Infrastructure;
using System.Text.Json;

namespace DBXPatching.Core
{
    public enum PatchingResultCode
    { 
        OK = 0,
        ERROR_INVALID_ARGUMENT =2,
        ERROR_INSERTING_RECORD_IN_DB = 3,
        ERROR_REFERENCE_NOT_FOUND = 4,
        ERROR_INVALID_REFERENCE_NAME = 5,
        ERROR_INVALID_REFERENCE_FIELD = 6,
        ERROR_SETTING_VALUE = 7,
        ERROR_INVALID_LOOKUP_INSRUCTION = 8,
        ERROR_LOOKUP_FAILED = 9,
        ERROR_INVALID_FIELD_REFERENCE = 10,
        ERROR_INVALID_VALUE_FOR_FIELD = 11,
    }

    public class DBXPatchingOperationResult
    {
        public PatchingResultCode ResultCode { get; set; } = PatchingResultCode.OK;
        public string[] Messages { get; set;} = [];

        public static DBXPatchingOperationResult Ok {
            get { return new DBXPatchingOperationResult { ResultCode = PatchingResultCode.OK }; }
        }
    }

    public class DBXPatcher
    {
        public string DBCInputDirectory { get; set; }
        public string DBCOutputDirectory { get; set; }

        private readonly DBDProvider _dbdProvider;
        private readonly DBCProvider _dbcProvider;
        private readonly DBCD.DBCD _dbcd;

        private readonly Dictionary<string, IDBCDStorage> openedFiles;

        private readonly Dictionary<string, int> _referenceIds;

        private readonly List<string> _modifiedFiles;

        public DBXPatcher(string inputDir, string outputDir)
        {
            DBCInputDirectory = inputDir;
            DBCOutputDirectory = outputDir;

            openedFiles = [];
            _referenceIds = [];
            _modifiedFiles = [];

            _dbdProvider = new DBDProvider();
            _dbcProvider = new DBCProvider();
            _dbcd = new DBCD.DBCD(_dbcProvider, _dbdProvider);
        }

        public DBXPatchingOperationResult ApplyPatch(Patch patch)
        {
            foreach (var instruction in patch.Lookup)
            {
                var result = ApplyLookupRecordInstructions(instruction);
                if (result.ResultCode != PatchingResultCode.OK)
                {
                    return result;
                }
            }
            foreach (var instruction in patch.Add)
            {
                var result = ApplyAddRecordInstructions(instruction);
                if (result.ResultCode != PatchingResultCode.OK)
                {
                    return result;
                }
            }

            foreach(var file in _modifiedFiles)
            {
                openedFiles[file].Save(Path.Join(DBCOutputDirectory, file));
            }

            return DBXPatchingOperationResult.Ok;
        }

        private DBXPatchingOperationResult ApplyLookupRecordInstructions(LookupRecordInstruction instruction)
        {
            var records = OpenDb(instruction.Filename);
            if (string.IsNullOrEmpty(instruction.Field) || instruction.SearchValue == null)
            {
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_INVALID_LOOKUP_INSRUCTION,
                    Messages = [$"Found lookup instruction without with invalid field or searchvalue for file '{instruction.Filename}'."]
                };
            } 
            if (instruction.SearchValue is JsonElement element)
            {
                var convertResult = ConvertJsonToFieldType(element, instruction.Filename, instruction.Field, out var convertedVal);
                if (convertResult.ResultCode != PatchingResultCode.OK)
                {
                    return convertResult;
                }
                instruction.SearchValue = convertedVal!;
            }
            foreach (var row in records.Values)
            {
                if (row[instruction.Field].Equals(instruction.SearchValue))
                {
                    ProcessSaveReferences(row, instruction.SaveReferences);
                    return DBXPatchingOperationResult.Ok;
                }
            }

            return new DBXPatchingOperationResult() {
                ResultCode = PatchingResultCode.ERROR_LOOKUP_FAILED
            };
        }

        private DBXPatchingOperationResult ApplyAddRecordInstructions(AddRecordInstruction instruction)
        {
            var records = OpenDb(instruction.Filename);

            records.AddEmpty();
            var row = records.Values.LastOrDefault();
            if (row == null)
            {
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_INSERTING_RECORD_IN_DB,
                    Messages = [$"Unable to add a record to file '{instruction.Filename}'"]
                };
            }

            if (instruction.RecordId.HasValue)
            {
                row.ID = instruction.RecordId.Value; 
                row[instruction.Filename, "ID"] = instruction.RecordId.Value;
            }

            if (!string.IsNullOrEmpty(instruction.RecordIdReference))
            {
                row.ID = _referenceIds[instruction.RecordIdReference];
                row[instruction.Filename, "ID"] = _referenceIds[instruction.RecordIdReference];
            }

            foreach(var generateId in instruction.GenerateIds)
            {
                if (string.IsNullOrEmpty(generateId.Name))
                {
                    return new DBXPatchingOperationResult()
                    {
                        ResultCode = PatchingResultCode.ERROR_INVALID_REFERENCE_NAME,
                        Messages = [$"Found generate id instruction without reference name for file '{instruction.Filename}'."]
                    };
                }
                if (string.IsNullOrEmpty(generateId.Field))
                {
                    return new DBXPatchingOperationResult()
                    {
                        ResultCode = PatchingResultCode.ERROR_INVALID_REFERENCE_FIELD,
                        Messages = [$"Found generate id instruction without reference field for file '{instruction.Filename}'."]
                    };
                }
                if (_referenceIds.ContainsKey(generateId.Name) && !generateId.OverrideExisting)
                {
                    continue;
                }
                var searchRecords = records;
                if (!string.IsNullOrEmpty(generateId.FileName))
                {
                    searchRecords = OpenDb(generateId.FileName);
                }
                _referenceIds[generateId.Name] = 1;
                foreach(var searchRow in searchRecords.Values)
                {
                    var compareVal = searchRow.FieldAs<int>(generateId.Field);
                    if (compareVal >= _referenceIds[generateId.Name])
                    {
                        _referenceIds[generateId.Name] = compareVal + 1;
                    }
                }
            }

            foreach (var col in instruction.Record)
            {
                try
                {
                    if (!string.IsNullOrEmpty(col.ReferenceId))
                    {
                        if (!_referenceIds.ContainsKey(col.ReferenceId))
                        {
                            if (col.FallBackValue == null)
                            {
                                return new DBXPatchingOperationResult()
                                {
                                    ResultCode = PatchingResultCode.ERROR_REFERENCE_NOT_FOUND,
                                    Messages = [$"Unable to find referenced instruction with name '{col.ReferenceId}'"]
                                };
                            }

                            if (col.FallBackValue is JsonElement element)
                            {
                                var convertResult = ConvertJsonToFieldType(element, instruction.Filename, col.ColumnName, out var convertedVal);
                                if (convertResult.ResultCode != PatchingResultCode.OK)
                                {
                                    return convertResult;
                                }
                                col.FallBackValue = convertedVal!;
                            }
                            row[instruction.Filename, col.ColumnName] = col.FallBackValue;
                        } 
                        else
                        {
                            row[instruction.Filename, col.ColumnName] = _referenceIds[col.ReferenceId];
                        }
                    }
                    else
                    {
                        if (col.Value is JsonElement element)
                        {
                            var convertResult = ConvertJsonToFieldType(element, instruction.Filename, col.ColumnName, out var convertedVal);
                            if (convertResult.ResultCode != PatchingResultCode.OK)
                            {
                                return convertResult;
                            }
                            col.Value = convertedVal!;
                        }
                        row[instruction.Filename, col.ColumnName] = col.Value;
                    }
                }
                catch
                {
                    return new DBXPatchingOperationResult()
                    {
                        ResultCode = PatchingResultCode.ERROR_SETTING_VALUE,
                        Messages = [$"Unable to set field '{col.ColumnName}' to '{col.Value}'"]
                    };
                }
            }

            ProcessSaveReferences(row, instruction.SaveReferences);
            if (!_modifiedFiles.Contains(instruction.Filename))
            {
                _modifiedFiles.Add(instruction.Filename);
            }
            return DBXPatchingOperationResult.Ok;
        }

        private IDBCDStorage OpenDb(string fileName)
        {
            var db2Name = Path.GetFileName(fileName);
            if (openedFiles.ContainsKey(db2Name))
            {
                return openedFiles[db2Name];
            }

            var db2Path = Path.Combine(DBCInputDirectory, db2Name);

            var dbdStream = _dbdProvider.StreamForTableName(db2Path);
            var dbdReader = new DBDReader();
            var databaseDefinition = dbdReader.Read(dbdStream);

            var storage = _dbcd.Load(db2Path, "9.2.7.45745", Locale.EnUS);
            openedFiles[db2Name] = storage;
            return storage;
        }

        private void ProcessSaveReferences(DBCDRow row, List<ReferenceColumnData> instructions)
        {
            foreach (var reference in instructions)
            {
                if (!string.IsNullOrEmpty(reference.Name))
                {
                    if (string.IsNullOrEmpty(reference.Field))
                    {
                        _referenceIds[reference.Name] = row.ID;
                    }
                    else
                    {
                        _referenceIds[reference.Name] = Convert.ToInt32(row[reference.Field]);
                    }
                }
            }
        }

        public DBXPatchingOperationResult ConvertJsonToFieldType(JsonElement element, string fileName, string field, out object? resultValue)
        {
            var fieldInfo = openedFiles[fileName].GetRowType().GetField(field);
            while (fieldInfo == null && field.Length > 0)
            {
                field = field.Remove(field.Length - 1);
                fieldInfo = openedFiles[fileName].GetRowType().GetField(field);
            }
            if (fieldInfo == null)
            {
                resultValue = null;
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_INVALID_FIELD_REFERENCE,
                    Messages = [$"Found instruction with invalid field reference '{field}' for file '{fileName}'."]
                };
            }
            var resultType = fieldInfo.FieldType;
            if (resultType.IsArray)
            {
                resultType = resultType.GetElementType()!;
            }
            resultValue = element.Deserialize(resultType);
            if (resultValue == null)
            {
                return new DBXPatchingOperationResult()
                {
                    ResultCode = PatchingResultCode.ERROR_INVALID_VALUE_FOR_FIELD,
                    Messages = [$"Found instruction without with invalid value '{element}' for file '{fileName}'."]
                };
            }
            return DBXPatchingOperationResult.Ok;
        }
    }
}
