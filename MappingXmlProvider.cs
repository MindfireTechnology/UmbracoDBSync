using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace UmbracoDbSync
{
	public class MappingXmlProvider
	{
		public string FileName { get; set; }

		public string DataContextName { get; protected set; }
		public string Assembly { get; set; }

		public IList<TableMapping> Mappings { get; set; }

		public MappingXmlProvider(string fileName)
		{
			Mappings = new List<TableMapping>();
			FileName = fileName;
			MapXmlFile();
		}

		protected void MapXmlFile()
		{
			using (Stream fs = File.Open(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				var doc = XDocument.Load(fs, LoadOptions.PreserveWhitespace);
				string ns = doc.Element("mappings").Attribute("namespace") != null ? doc.Element("mappings").Attribute("namespace").Value : null;
				Assembly = doc.Element("mappings").Attribute("assembly").Value;
				DataContextName = doc.Element("mappings").Attribute("dataContext").Value;


				foreach (XElement table in doc.Element("mappings").Descendants("table"))
				{
					ProcessTableMapping(table, ns);
				}
			}
		}

		protected void ProcessTableMapping(XElement table, string defaultNamespace)
		{
			var tableMap = new TableMapping();
			if (table.Attribute("name") == null)
				throw new ArgumentException("Table attribute 'name' is required!");

			tableMap.Name = table.Attribute("name").Value;
			MapValue(table, "documentType", n => tableMap.DocumentType = n);
			MapValue(table, "entityTypeName", n => tableMap.EntityTypeFullName = n);
			MapValue(table, "autoMap", n => tableMap.AutoMapFields = bool.Parse(n));
			MapValue(table, "allowDelete", n => tableMap.AllowDelete = bool.Parse(n));
			MapValue(table, "entityPropertyName", n => tableMap.EntityPropertyName = n);
			MapValue(table, "namespace", n => tableMap.Namespace = n);
			MapValue(table, "assemlby", n => tableMap.Assembly = n);

			if (string.IsNullOrWhiteSpace(tableMap.Namespace))
				tableMap.Namespace = defaultNamespace;

			if (string.IsNullOrWhiteSpace(tableMap.Assembly))
				tableMap.Assembly = Assembly;

			// Normalization
			if (string.IsNullOrWhiteSpace(tableMap.EntityPropertyName))
				tableMap.EntityPropertyName = tableMap.Name;

			tableMap.EntityTypeFullName = tableMap.Namespace + "." + (string.IsNullOrWhiteSpace(tableMap.EntityTypeFullName) ?
				tableMap.Name : tableMap.EntityTypeFullName);

			// Children
			foreach (var field in table.Descendants())
			{
				ProcessFieldMapping(tableMap, field);
			}
			Mappings.Add(tableMap);
		}

		protected void ProcessFieldMapping(TableMapping tableMap, XElement field)
		{
			var fieldMap = new FieldMapping { Table = tableMap };

			MapValue(field, "name", n => fieldMap.Name = n);
			MapValue(field, "alias", n => fieldMap.Alias = n);
			MapValue(field, "key", n => fieldMap.Key = bool.Parse(n));
			MapValue(field, "isEnabledColumn", n => fieldMap.IsEnabledColumn = bool.Parse(n));
			MapValue(field, "inherit", n => fieldMap.Inherit = bool.Parse(n));
			MapValue(field, "dataType", n => fieldMap.FieldType = (DataType)Enum.Parse(typeof(DataType), n, true));
			
			MapValue(field, "defaultValue", n => 
			{
				fieldMap.DefaultValue = MapStringToValue(fieldMap.FieldType, n);
			});

			// Normalization
			if (string.IsNullOrWhiteSpace(fieldMap.Alias))
				fieldMap.Alias = fieldMap.Name;

			tableMap.FieldMappings.Add(fieldMap);
		}

		protected void MapValue(XElement node, string attributeName, Action<string> assign)
		{
			if (node.Attribute(attributeName) != null)
				assign(node.Attribute(attributeName).Value);
		}

		protected object MapStringToValue(DataType? type, string value)
		{
			switch(type)
			{
				case DataType.Integer:
					return int.Parse(value);
				case DataType.DateTime:
					return DateTime.Parse(value);
				case DataType.Boolean:
					return bool.Parse(value);
				case DataType.String:
				case DataType.DateTimeStamp:
				default:
					return value;
			}
		}
	}
}
