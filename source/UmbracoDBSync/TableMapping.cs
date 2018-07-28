using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmbracoDbSync
{
	public class TableMapping
	{
		/// <summary>Table Name</summary>
		public string Name { get; set; }
		public string DocumentType { get; set; }
		public string Assembly { get; set; }
		public string Namespace { get; set; }
		public string EntityTypeFullName { get; set; }
		public string EntityTypeName { get { return EntityTypeFullName.Substring(EntityTypeFullName.LastIndexOf(".") + 1); } }
		public string EntityPropertyName { get; set; }

		public bool AutoMapFields { get; set; }
		public bool AllowDelete { get; set; }

		public IList<FieldMapping> FieldMappings { get; set; }

		public TableMapping()
		{
			FieldMappings = new List<FieldMapping>();
			AutoMapFields = true;
		}
	}
}
