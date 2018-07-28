using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmbracoDbSync
{
	public class FieldMapping
	{
		public TableMapping Table { get; set; }
		/// <summary>Field Name</summary>
		public string Name { get; set; }
		public string Alias { get; set; }
		public bool Key { get; set; }
		public bool IsEnabledColumn { get; set; }
		public bool Inherit { get; set; }
		public object DefaultValue { get; set; }
		public DataType? FieldType { get; set; }
	}
}
