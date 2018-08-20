using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Web;
using System.Web.Security;
using Umbraco.Core;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

namespace UmbracoDbSync
{
	/// <summary>
	/// This class will sync data from Umbraco to a database based on configuration & Mappings
	/// </summary>
	public sealed class OneWayDataSync : ApplicationEventHandler
	{
		public MappingXmlProvider MapProvider { get; set; }
		public Func<DbContext> ContextFunc { get; set; }


		protected override void ApplicationStarted(UmbracoApplicationBase umbracoApplication, ApplicationContext applicationContext)
		{
			base.ApplicationStarted(umbracoApplication, applicationContext);

			string configFile = Directory.EnumerateFiles(GetHostPath(), "TableMappings.config", SearchOption.AllDirectories).FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(configFile))
				Initialize(configFile);
		}

		private void Initialize(string xmlFileName)
		{
			MapProvider = new MappingXmlProvider(xmlFileName);
			ContextFunc = () =>
			{
				DbContext context = (DbContext)CreateObjectFromTypeName(MapProvider.Assembly, MapProvider.DataContextName);
				return context;
			};

			ContentService.Saving += ContentService_Saving;
			ContentService.Deleting += ContentService_Deleting;
			ContentService.Trashed += ContentService_Trashed;
		}
		private string GetHostPath()
		{
			// Getting the path in a somewhat platform agnostic way.
			return HttpContext.Current.Server.MapPath("~/");
		}

		private void ContentService_Trashed(IContentService sender, MoveEventArgs<IContent> e)
		{
			// We don't really support the recycle bin here. If they delete it, we ask the system to actually delete it.
			foreach (var entry in e.MoveInfoCollection)
			{
				string documentType = entry.Entity.ContentType.Alias;
				var mapping = MapProvider.Mappings.SingleOrDefault(n => n.DocumentType == documentType);
				if (mapping != null)
					ApplicationContext.Current.Services.ContentService.Delete(entry.Entity);
			}
		}


		private void ContentService_Deleting(IContentService sender, DeleteEventArgs<IContent> e)
		{
			foreach (var entity in e.DeletedEntities)
			{
				string documentType = entity.ContentType.Alias;
				var mapping = MapProvider.Mappings.SingleOrDefault(n => n.DocumentType == documentType);
				if (mapping != null)
					DeleteDocument(mapping, sender, entity);
			}
		}

		private void ContentService_Saving(IContentService sender, SaveEventArgs<IContent> e)
		{
			foreach (var doc in e.SavedEntities)
			{
				var mapping = MapProvider.Mappings.SingleOrDefault(n => n.DocumentType.Equals(doc.ContentType.Alias, StringComparison.CurrentCultureIgnoreCase));
				if (mapping != null)
					SaveDocument(mapping, sender, doc);
			}
		}

		private void SaveDocument(TableMapping mapping, IContentService service, IContent document)
		{
			// Look for a document that already exists
			var keyProp = mapping.FieldMappings.SingleOrDefault(n => n.Key);

			int key;
			bool updating = int.TryParse(GetDocumentValue(document, keyProp.Alias), out key);
			using (var context = ContextFunc())
			{
				dynamic entity;
				dynamic dbset = GetPropertyValueByName(context, mapping.EntityPropertyName);

				if (!updating)
					entity = CreateObjectFromTypeName(mapping.Assembly, mapping.EntityTypeFullName);
				else
					entity = dbset.Find(key);

				// Apply the mappings!
				SyncDocument(mapping, service, document, entity);

				if (!updating)
					dbset.Add(entity);

				context.SaveChanges();

				// Set the newly generated key (when applicable)
				if (!updating)
					SetDocumentValue(document, keyProp.Alias, GetPropertyValueByName(entity, keyProp.Name));
			}
		}

		private object CreateObjectFromTypeName(string typeFullName)
		{
			string[] typeParts = typeFullName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			if (typeParts.Length != 2)
				throw new ArgumentException(string.Format("Invalid full type name '{0}'", typeFullName));

			string typeName = typeParts[0].Trim();
			string assemblyName = typeParts[1].Trim();

			return CreateObjectFromTypeName(assemblyName, typeName);
		}

		private object CreateObjectFromTypeName(string assembly, string typeName)
		{
			ObjectHandle oh = Activator.CreateInstance(assembly, typeName);
			return oh.Unwrap();
		}

		private string GetDocumentValue(IContent document, string property)
		{
			if (property.Equals("Name", StringComparison.CurrentCultureIgnoreCase))
				return document.Name;

			if (property.Equals("Path", StringComparison.CurrentCultureIgnoreCase))
				return document.Path;

			// Look for the property
			var prop = document.Properties.SingleOrDefault(n => n.Alias.Equals(property, StringComparison.CurrentCultureIgnoreCase));
			if (prop == null)
				return null;
			else
				return prop.Value != null ? prop.Value.ToString() : null;
		}

		private void SetDocumentValue(IContent document, string property, object value)
		{
			var prop = document.Properties.SingleOrDefault(n => n.Alias.Equals(property, StringComparison.CurrentCultureIgnoreCase));
			if (prop != null)
				prop.Value = value;
		}

		private void DeleteDocument(TableMapping mapping, IContentService service, IContent document)
		{
			// Look for a document that already exists
			var keyProp = mapping.FieldMappings.SingleOrDefault(n => n.Key);

			int key;
			bool found = int.TryParse(document.Properties[keyProp.Name].Value.ToString(), out key);
			if (found)
			{
				using (var context = ContextFunc())
				{
					dynamic dbset = GetPropertyValueByName(context, mapping.EntityPropertyName);
					dynamic entity = dbset.Find(key);

					// Does the database even have this entity anymore?
					if (entity == null)
						return;

					if (mapping.FieldMappings.Any(n => n.IsEnabledColumn))
					{
						SetValue(entity, false, mapping.FieldMappings.Single(n => n.IsEnabledColumn).Name);
					}
					else
						dbset.Remove(entity);

					context.SaveChanges();
				}
			}
		}

		private object GetPropertyValueByName(object target, string propertyName)
		{
			return GetPropertyByName(target, propertyName).GetValue(target, null);
		}

		private PropertyInfo GetPropertyByName(object target, string propertyName)
		{
			return target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).SingleOrDefault(n => n.Name.Equals(propertyName, StringComparison.CurrentCultureIgnoreCase));
		}

		private object GetPropertyTypeByName(object target, string propertyName)
		{
			return GetPropertyByName(target, propertyName).PropertyType;
		}

		private bool ObjectHasProperty(object target, string propertyName)
		{
			return GetPropertyByName(target, propertyName) != null;
		}

		private void SetValue(object target, object value, string propertyName)
		{
			Debug.WriteLine(string.Format("Mapping {0} property {1} to value {2}", target, propertyName, value));

			if (value != null && !string.IsNullOrWhiteSpace(propertyName))
			{
				var prop = GetPropertyByName(target, propertyName);

				if (prop.PropertyType == typeof(bool))
					value = (value?.ToString() == "1" || (value != null && value.ToString().Equals("true", StringComparison.CurrentCultureIgnoreCase))) ? true : false;

				object setVal = null;
				if (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
					setVal = Convert.ChangeType(value, prop.PropertyType.GetGenericArguments()[0]);
				else
					setVal = Convert.ChangeType(value, prop.PropertyType);

				prop.SetValue(target, setVal, null);
			}
		}

		private void SyncDocument(TableMapping mapping, IContentService service, IContent document, dynamic entity)
		{
			var completed = new List<string>();

			// Try apply all values from the document to the entity (Explicit, from Map)
			foreach (var prop in mapping.FieldMappings)
			{
				completed.Add(prop.Alias);
				if (prop.Key)
					continue;

				object value = GetDocumentValue(document, prop.Alias);
				if (value != null)
				{
					SetValue(entity, value, prop.Name);
					continue;
				}

				// Default Value
				if (prop.DefaultValue != null)
				{
					if (prop.FieldType == DataType.String)
					{
						if (string.IsNullOrWhiteSpace(GetPropertyValueByName(entity, prop.Name)))
							SetValue(entity, prop.DefaultValue, prop.Name);
					}
					else if (prop.FieldType == DataType.Integer)
					{
						if (0 == GetPropertyValueByName(entity, prop.Name) || GetPropertyValueByName(entity, prop.Name) == null)
							SetValue(entity, prop.DefaultValue, prop.Name);
					}
					else if (prop.FieldType == DataType.DateTime)
					{
						if (DateTime.MinValue == GetPropertyValueByName(entity, prop.Name) || GetPropertyValueByName(entity, prop.Name) == null)
							SetValue(entity, prop.DefaultValue, prop.Name);
					}
					else if (prop.FieldType == DataType.Boolean)
					{
						SetValue(entity, prop.DefaultValue, prop.Name);
					}
					else if (prop.FieldType == DataType.DateTimeStamp)
					{
						SetValue(entity, DateTime.Now, prop.Name);
					}
				}

				// Inherited?
				if (prop.Inherit)
				{
					if (prop.Alias == "UserID" && Membership.GetUser() != null)
						SetValue(entity, Membership.GetUser().ProviderUserKey, prop.Name);
					else
					{
						value = SearchAncestorForProperty(document, prop.Alias);
						if (value != null)
							SetValue(entity, value, prop.Name);
					}
				}
			}

			// Try apply all values from the document to the entity (Implicit / Auto Mapped)
			if (mapping.AutoMapFields)
				foreach (var prop in document.Properties)
				{
					try
					{
						if (!completed.Contains(prop.Alias) && ObjectHasProperty(entity, prop.Alias))
						{
							var objType = GetPropertyTypeByName(entity, prop.Alias);
							if (objType != null && (objType.IsValueType || objType == typeof(string)))
								SetValue(entity, prop.Value, prop.Alias);
						}
					}
					catch (NullReferenceException) { /* no way to check for a document property without an exception */ }
				}
		}

		private object SearchAncestorForProperty(IContent document, string alias)
		{
			if (document.Properties.Any(n => n.Alias == alias))
				return document.Properties[alias].Value;
			else if (document.Parent() != null)
				return SearchAncestorForProperty(document.Parent(), alias);
			else
				return null;
		}

	}
}
