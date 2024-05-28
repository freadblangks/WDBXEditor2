using DBCD.Providers;

namespace DBXPatching.Core.Infrastructure
{
    public class DBCProvider : IDBCProvider
    {
        public Stream StreamForTableName(string tableName, string build) => File.OpenRead(tableName);
    }
}
