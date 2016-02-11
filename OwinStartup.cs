using Microsoft.Owin;
using Owin;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

[assembly: OwinStartup(typeof(UmbracoDbSync.OwinStartup), "DbSyncStartup")]

namespace UmbracoDbSync
{
	public sealed class OwinStartup
	{
		private OneWayDataSync Sync;

		public void DbSyncStartup(IAppBuilder app)
		{
			string configFile = Directory.EnumerateFiles(GetHostPath(), "TableMappings.config", SearchOption.AllDirectories).FirstOrDefault();
			string EntityFrameworkContext = ConfigurationManager.AppSettings["DbSyncEFFullTypeName"];

			if (!string.IsNullOrWhiteSpace(configFile))
			{
				// Entity Framework
				Sync = new OneWayDataSync(configFile); 
			}
		}

		private string GetHostPath()
		{
			// Getting the path in a somewhat platform agnostic way.
			return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		}
	}
}
