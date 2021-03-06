﻿using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Net;
using System.Text;
using Stratis.Bitcoin.Logging;

namespace Stratis.Bitcoin.Configuration
{
	public class RPCArgs
	{
		public RPCArgs()
		{
			Bind = new List<IPEndPoint>();
			AllowIp = new List<IPAddress>();
		}

		public int RPCPort
		{
			get; set;
		}
		public List<IPEndPoint> Bind
		{
			get; set;
		}

		public List<IPAddress> AllowIp
		{
			get; set;
		}

		public string[] GetUrls()
		{
			return Bind.Select(b => "http://" + b + "/").ToArray();
		}
	}
	public class NodeArgs
	{
		public RPCArgs RPC
		{
			get; set;
		}
		public bool Testnet
		{
			get; set;
		}
		public string DataDir
		{
			get; set;
		}
		public bool RegTest
		{
			get;
			set;
		}
		public string ConfigurationFile
		{
			get;
			set;
		}

		public static NodeArgs GetArgs(string[] args)
		{
			NodeArgs nodeArgs = new NodeArgs();
			nodeArgs.ConfigurationFile = args.Where(a => a.StartsWith("-conf=")).Select(a => a.Substring("-conf=".Length).Replace("\"", "")).FirstOrDefault();
			nodeArgs.DataDir = args.Where(a => a.StartsWith("-datadir=")).Select(a => a.Substring("-datadir=".Length).Replace("\"", "")).FirstOrDefault();
			if(nodeArgs.DataDir != null)
			{
				nodeArgs.ConfigurationFile = Path.Combine(nodeArgs.DataDir, nodeArgs.ConfigurationFile);
			}
			nodeArgs.Testnet = args.Contains("-testnet", StringComparer.CurrentCultureIgnoreCase);
			nodeArgs.RegTest = args.Contains("-regtest", StringComparer.CurrentCultureIgnoreCase);

			if(nodeArgs.ConfigurationFile != null)
			{
				AssetConfigFileExists(nodeArgs);
				var configTemp = TextFileConfiguration.Parse(File.ReadAllText(nodeArgs.ConfigurationFile));
				nodeArgs.Testnet = configTemp.GetOrDefault<bool>("testnet", false);
				nodeArgs.RegTest = configTemp.GetOrDefault<bool>("regtest", false);
			}

			var network = nodeArgs.GetNetwork();
			if(nodeArgs.DataDir == null)
			{
				nodeArgs.DataDir = GetDefaultDataDir("stratisbitcoin", network);
			}

			if(nodeArgs.ConfigurationFile == null)
			{
				nodeArgs.ConfigurationFile = nodeArgs.GetDefaultConfigurationFile();
			}

			Logs.Configuration.LogInformation("Data directory set to " + nodeArgs.DataDir);
			Logs.Configuration.LogInformation("Configuration file set to " + nodeArgs.ConfigurationFile);

			if(!Directory.Exists(nodeArgs.DataDir))
				throw new ConfigurationException("Data directory does not exists");			

			var consoleConfig = new TextFileConfiguration(args);
			var config = TextFileConfiguration.Parse(File.ReadAllText(nodeArgs.ConfigurationFile));
			consoleConfig.MergeInto(config);

			nodeArgs.RPC = config.GetOrDefault<bool>("server", false) ? new RPCArgs() : null;
			if(nodeArgs.RPC != null)
			{
				var defaultPort = config.GetOrDefault<int>("rpcport", network.RPCPort);
				nodeArgs.RPC.RPCPort = defaultPort;
				try
				{
					nodeArgs.RPC.Bind = config
									.GetAll("rpcbind")
									.Select(p => ConvertToEndpoint(p, defaultPort))
									.ToList();
				}
				catch(FormatException)
				{
					throw new ConfigurationException("Invalid rpcbind value");
				}

				try
				{

					nodeArgs.RPC.AllowIp = config
									.GetAll("rpcallowip")
									.Select(p => IPAddress.Parse(p))
									.ToList();
				}
				catch(FormatException)
				{
					throw new ConfigurationException("Invalid rpcallowip value");
				}

				if(nodeArgs.RPC.AllowIp.Count == 0)
				{
					nodeArgs.RPC.Bind.Clear();
					nodeArgs.RPC.Bind.Add(new IPEndPoint(IPAddress.Parse("::1"), defaultPort));
					nodeArgs.RPC.Bind.Add(new IPEndPoint(IPAddress.Parse("127.0.0.1"), defaultPort));
					if(config.Contains("rpcbind"))
						Logs.Configuration.LogWarning("WARNING: option -rpcbind was ignored because -rpcallowip was not specified, refusing to allow everyone to connect");
				}

				if(nodeArgs.RPC.Bind.Count == 0)
				{
					nodeArgs.RPC.Bind.Add(new IPEndPoint(IPAddress.Parse("::"), defaultPort));
					nodeArgs.RPC.Bind.Add(new IPEndPoint(IPAddress.Parse("0.0.0.0"), defaultPort));
				}
			}
			var folder = new DataFolder(nodeArgs.DataDir);
			if(!Directory.Exists(folder.CoinViewPath))
				Directory.CreateDirectory(folder.CoinViewPath);
			return nodeArgs;
		}

		private static void AssetConfigFileExists(NodeArgs nodeArgs)
		{
			if(!File.Exists(nodeArgs.ConfigurationFile))
				throw new ConfigurationException("Configuration file does not exists");
		}

		static IPEndPoint ConvertToEndpoint(string str, int defaultPort)
		{
			var portOut = defaultPort;
			var hostOut = "";
			int colon = str.LastIndexOf(':');
			// if a : is found, and it either follows a [...], or no other : is in the string, treat it as port separator
			bool fHaveColon = colon != -1;
			bool fBracketed = fHaveColon && (str[0] == '[' && str[colon - 1] == ']'); // if there is a colon, and in[0]=='[', colon is not 0, so in[colon-1] is safe
			bool fMultiColon = fHaveColon && (str.LastIndexOf(':', colon - 1) != -1);
			if(fHaveColon && (colon == 0 || fBracketed || !fMultiColon))
			{
				int n;
				if(int.TryParse(str.Substring(colon + 1), out n) && n > 0 && n < 0x10000)
				{
					str = str.Substring(0, colon);
					portOut = n;
				}
			}
			if(str.Length > 0 && str[0] == '[' && str[str.Length - 1] == ']')
				hostOut = str.Substring(1, str.Length - 2);
			else
				hostOut = str;
			return new IPEndPoint(IPAddress.Parse(str), defaultPort);
		}

		private string GetDefaultConfigurationFile()
		{
			var config = Path.Combine(DataDir, "bitcoin.conf");
			Logs.Configuration.LogInformation("Configuration file set to " + config);
			if(!File.Exists(config))
			{
				Logs.Configuration.LogInformation("Creating configuration file");

				StringBuilder builder = new StringBuilder();
				builder.AppendLine("####RPC Settings####");
				builder.AppendLine("#Activate RPC Server (default: 0)");
				builder.AppendLine("#server=0");
				builder.AppendLine("#Where the RPC Server binds (default: 127.0.0.1 and ::1)");
				builder.AppendLine("#rpcbind=127.0.0.1");
				builder.AppendLine("#Ip address allowed to connect to RPC (default all: 0.0.0.0 and ::)");
				builder.AppendLine("#rpcallowedip=127.0.0.1");
				File.WriteAllText(config, builder.ToString());
			}
			return config;
		}

		public Network GetNetwork()
		{
			return Testnet ? Network.TestNet :
				RegTest ? Network.RegTest :
				Network.Main;
		}

		private static string GetDefaultDataDir(string appName, Network network)
		{
			string directory = null;
			var home = Environment.GetEnvironmentVariable("HOME");
			if(!string.IsNullOrEmpty(home))
			{
				Logs.Configuration.LogInformation("Using HOME environment variable for initializing application data");
				directory = home;
				directory = Path.Combine(directory, "." + appName.ToLowerInvariant());
			}
			else
			{
				var localAppData = Environment.GetEnvironmentVariable("APPDATA");
				if(!string.IsNullOrEmpty(localAppData))
				{
					Logs.Configuration.LogInformation("Using APPDATA environment variable for initializing application data");
					directory = localAppData;
					directory = Path.Combine(directory, appName);
				}
				else
				{
					throw new DirectoryNotFoundException("Could not find suitable datadir");
				}
			}
			if(!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			directory = Path.Combine(directory, network.Name);
			if(!Directory.Exists(directory))
			{
				Logs.Configuration.LogInformation("Creating data directory");
				Directory.CreateDirectory(directory);
			}
			return directory;
		}
	}
}
