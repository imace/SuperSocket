﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using SuperSocket.Common;
using SuperSocket.SocketServiceCore.Config;
using System.Configuration;
using System.Reflection;
using System.IO;
using System.ServiceModel.Description;
using System.ServiceModel.Security;
using System.Security.Cryptography.X509Certificates;

namespace SuperSocket.SocketServiceCore
{
    public static class SocketServerManager
    {
        private static List<IRunable> m_ServerList = new List<IRunable>();

        private static Dictionary<string, Type> m_ServiceDict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        private static IConfig m_Config;

        /// <summary>
        /// Initializes with the specified config.
        /// </summary>
        /// <param name="config">The config.</param>
        /// <returns></returns>
        public static bool Initialize(IConfig config)
        {
            m_Config = config;

            List<IServiceConfig> serviceList = config.GetServiceList();

            Type serviceType;

            foreach (IServiceConfig service in serviceList)
            {
                if (service.Disabled)
                    continue;

                bool initResult = false;

				if (AssemblyUtil.TryGetType<IRunable>(service.BaseAssembly, out serviceType))
                {
                    initResult = true;
					m_ServiceDict[service.ServiceName] = serviceType;
                }

                if (!initResult)
                {
                    LogUtil.LogError("Failed to initialize " + service.ServiceName + "!");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Starts with specified config.
        /// </summary>
        /// <param name="config">The config.</param>
        /// <returns></returns>
        public static bool Start(IConfig config)
        {
            List<IServerConfig> serverList = config.GetServerList();

            Type serviceType = null;

			ServiceCredentials credentials = GetServiceCredentials(config.CredentialConfig);

            foreach (IServerConfig serverConfig in serverList)
            {
                if (serverConfig.Disabled)
                    continue;

                bool startResult = false;

				if (m_ServiceDict.TryGetValue(serverConfig.ServiceName, out serviceType))
                {
					IRunable server = Activator.CreateInstance(serviceType) as IRunable;					
					if (server != null && server.Setup(GetServiceProvider(serverConfig.ServiceName, serverConfig.Provider), serverConfig))
                    {
                        server.ServerCredentials = credentials;
                        if (server.Start())
                        {
                            m_ServerList.Add(server);
                            startResult = true;
                        }
                    }
                }

				if (!startResult)
				{
					LogUtil.LogError("Failed to start " + serverConfig.Name + " server!");
					return false;
				}
				else
				{
					LogUtil.LogInfo(serverConfig.Name + " has been started");
				}
            }

            return true;
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public static void Stop()
        {
            foreach (IRunable server in m_ServerList)
            {
                server.Stop();
            }
        }

        public static IServiceConfig GetServiceConfig(string name)
        {
            foreach (IServiceConfig config in m_Config.GetServiceList())
            {
                if (string.Compare(config.ServiceName, name, true) == 0)
                {
                    return config;
                }
            }
            return null;
        }

        public static string GetServiceProvider(string service, string provider)
        {
            IServiceConfig config = GetServiceConfig(service);

            if (config == null)
                return string.Empty;

            NameValueConfigurationElement element = config.Providers[provider];

            if (element == null)
                return string.Empty;
            else
                return element.Value;
        }

		public static ServiceCredentials GetServiceCredentials(ICredentialConfig config)
		{
			ServiceCredentials credential = new ServiceCredentials();

			try
			{
				credential.UserNameAuthentication.UserNamePasswordValidationMode = UserNamePasswordValidationMode.Custom;
				credential.UserNameAuthentication.CachedLogonTokenLifetime = TimeSpan.FromHours(1);
				credential.UserNameAuthentication.CacheLogonTokens = true;
				credential.UserNameAuthentication.CustomUserNamePasswordValidator = new SocketManagerPasswordValidator(config);
				credential.ServiceCertificate.SetCertificate(StoreLocation.LocalMachine, StoreName.My, X509FindType.FindBySubjectName, "GiantSocketServer");
			}
			catch (Exception e)
			{
				//X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
				//store.Open(OpenFlags.ReadWrite);
				//LogUtil.LogInfo("All certificates count:" + store.Certificates.Count);
				//X509Certificate2Collection certs = store.Certificates.Find(X509FindType.FindBySubjectName, "localhost", false);
				//LogUtil.LogInfo("Certificates count:" + certs.Count);
				//if (certs != null && certs.Count > 1)
				//{
				//    store.Remove(certs[0]);
				//    store.Close();
				//}
				LogUtil.LogError(e);
				credential = null;
			}

			return credential;
		}
    }
}
