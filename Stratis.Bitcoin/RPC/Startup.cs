﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NBitcoin.JsonConverters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.RPC
{
	public class Startup
	{
		// This method gets called by the runtime. Use this method to add services to the container.
		// For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddSingleton<IObjectModelValidator, NoObjectModelValidator>();			
			services.AddMvcCore(o =>
			{
				o.ValueProviderFactories.Clear();
				o.ValueProviderFactories.Add(new RPCParametersValueProvider());
			})
				.AddJsonFormatters()				
				.AddFormatterMappings();
			services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, RPCJsonMvcOptionsSetup>());
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IHostingEnvironment env,
			ILoggerFactory loggerFactory,
			IServiceProvider serviceProvider)
		{
			var logging = new FilterLoggerSettings();
			//logging.Add("Microsoft.AspNetCore.Hosting.Internal.WebHost", LogLevel.Error);
			//logging.Add("Microsoft.AspNetCore.Mvc", LogLevel.Error);
			loggerFactory
				//.WithFilter(logging)
				.AddConsole();

			if(env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			app.UseRPC();

			var fullNode = serviceProvider.GetService<FullNode>();
			var options = GetMVCOptions(serviceProvider);
			Serializer.RegisterFrontConverters(options.SerializerSettings, fullNode.Network);
			app.UseMiddleware(typeof(RPCMiddleware));
		}
		
		private static MvcJsonOptions GetMVCOptions(IServiceProvider serviceProvider)
		{
			return serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MvcJsonOptions>>().Value;
		}
	}

	class NoObjectModelValidator : IObjectModelValidator
	{
		public void Validate(ActionContext actionContext, ValidationStateDictionary validationState, string prefix, object model)
		{

		}
	}
}
