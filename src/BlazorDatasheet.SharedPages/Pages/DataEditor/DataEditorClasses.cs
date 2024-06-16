#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ADefWebserverDataEditor
{
    public class ConnectionSetting
    {
        public string DatabaseName { get; set; }
        public string ServerName { get; set; }
        public bool IntegratedSecurity { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class DTOStatus
    {
        public string StatusMessage { get; set; }
        public bool Success { get; set; }
    }

    public class DTODatabaseColumn
    {
        public string ColumnName { get; set; }
        public string ColumnType { get; set; }
        public int ColumnLength { get; set; }
        public bool IsPrimaryKey { get; set; }
    }
}
