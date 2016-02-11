using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Reflection;
using System.Web.Security;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

namespace UmbracoDbSync
{
	/// <summary>
	/// This class will sync data from Umbraco to a database based on configuration & Mappings
	/// </summary>
	public class OneWayDataSync
	{
		public MappingXmlProvider MapProvider { get; set; }
		public Func<DbContext> ContextFunc { get; set; }

		public OneWayDataSync(string xmlFileName)
		{
			MapProvider = new MappingXmlProvider(xmlFileName);
			ContextFunc = () =>
			{
				Type dbContextType = Type.GetType(MapProvider.DataContextName);
				DbContext context = (DbContext)Activator.CreateInstance(dbContextType);
				return context;
			};

			ContentService.Created += ContentService_Created;
			ContentService.Deleting += ContentService_Deleting;
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

		private void ContentService_Created(IContentService service, NewEventArgs<IContent> e)
		{
			string documentType = e.Entity.ContentType.Alias;
			var mapping = MapProvider.Mappings.SingleOrDefault(n => n.DocumentType == documentType);
			if (mapping != null)
				SaveDocument(mapping, service, e.Entity);
		}

		private void SaveDocument(TableMapping mapping, IContentService service, IContent document)
		{
			// Look for a document that already exists
			var keyProp = mapping.FieldMappings.SingleOrDefault(n => n.Key);

			int key;
			bool updating = int.TryParse(document.Properties[keyProp.Alias].Value.ToString(), out key);
			using (var context = ContextFunc())
			{
				dynamic entity;
				dynamic dbset = GetPropertyByName(context, mapping.EntityPropertyName);

				if (!updating)
				{
					entity = Activator.CreateInstance(Type.GetType(mapping.EntityTypeFullName));
					dbset.Add(entity);
				}
				else
					entity = dbset.Find(key);

				// Apply the mappings!
				SyncDocument(mapping, service, document, entity);
				context.SaveChanges();

				// Set the key
				if (!updating)
				{
					document.Properties[keyProp.Alias].Value = GetPropertyByName(entity, keyProp.Name);
					service.Save(document); // May not be necessary
				}
			}
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
					dynamic dbset = GetPropertyByName(context, mapping.EntityPropertyName);
					dynamic entity = dbset.Find(key);

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

		private object GetPropertyByName(object target, string propertyName)
		{
			return target.GetType().GetProperty(propertyName).GetValue(target, null);
		}

		private object GetPropertyTypeByName(object target, string propertyName)
		{
			return target.GetType().GetProperty(propertyName).PropertyType;
		}

		private bool ObjectHasProperty(object target, string propertyName)
		{
			return target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Any(n => n.Name == propertyName);
		}

		private void SetValue(object target, object value, string propertyName)
		{
			if (value != null && !string.IsNullOrWhiteSpace(propertyName))
				target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value, null);
		}

		private void SyncDocument(TableMapping mapping, IContentService service, IContent document, dynamic entity)
		{
			var completed = new List<string>();

			// Try apply all values from the document to the entity (Explicit, from Map)
			foreach (var prop in mapping.FieldMappings)
			{
				completed.Add(prop.Alias);
				if (!prop.Key)
				{
					try
					{
						object value = document.Properties[prop.Alias].Value;
						SetValue(entity, value, prop.Name);
					}
					catch (NullReferenceException) { /* no way to check for a document property without an exception */ }

					// Default Value
					if (prop.DefaultValue != null)
					{
						if (prop.FieldType == DataType.String)
						{
							if (string.IsNullOrWhiteSpace(GetPropertyByName(entity, prop.Name)))
								SetValue(entity, prop.DefaultValue, prop.Name);
						}
						else if (prop.FieldType == DataType.Integer)
						{
							if (0 == GetPropertyByName(entity, prop.Name) || GetPropertyByName(entity, prop.Name) == null)
								SetValue(entity, prop.DefaultValue, prop.Name);
						}
						else if (prop.FieldType == DataType.DateTime)
						{
							if (DateTime.MinValue == GetPropertyByName(entity, prop.Name) || GetPropertyByName(entity, prop.Name) == null)
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
							var value = SearchAncestorForProperty(document, prop.Alias);
							if (value != null)
								SetValue(entity, value, prop.Name);
						}
					}
				}
			}

			// Try apply all values from the document to the entity (Implicit)
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
			else
				if (document.Level > 1)
				return SearchAncestorForProperty(document.Parent(), alias);
			else
				return null;
		}

	}
}
