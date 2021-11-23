﻿using Autofac;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Models.JwtBearer;
using Repository.Database;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using TencentService;
using TencentService._;
using Web.Auth;
using Web.Extension;
using Web.Extension.Autofac;
using Web.Filters;
using Web.Global.User;
using Web.Libraries.Swagger;
using Web.Subscribes;

namespace WebApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            //为各数据库注入连接字符串
            Repository.Database.dbContext.ConnectionString = Configuration.GetConnectionString("dbConnection");
            services.AddDbContextPool<Repository.Database.dbContext>(options => { }, 100);

            services.AddSingleton<DemoSubscribe>();
            services.AddCap(options =>
            {

                //使用 Redis 传输消息
                options.UseRedis(Configuration.GetConnectionString("redisConnection"));

                //var rabbitMQSetting = Configuration.GetSection("RabbitMQSetting").Get<RabbitMQSetting>();

                ////使用 RabbitMQ 传输消息
                //options.UseRabbitMQ(options =>
                //{
                //    options.HostName = rabbitMQSetting.HostName;
                //    options.UserName = rabbitMQSetting.UserName;
                //    options.Password = rabbitMQSetting.PassWord;
                //    options.VirtualHost = rabbitMQSetting.VirtualHost;
                //    options.Port = rabbitMQSetting.Port;
                //    options.ConnectionFactoryOptions = options =>
                //    {
                //        options.Ssl = new RabbitMQ.Client.SslOption { Enabled = rabbitMQSetting.Ssl.Enabled, ServerName = rabbitMQSetting.Ssl.ServerName };
                //    };
                //});


                //使用 ef 搭配 db 存储执行情况
                options.UseEntityFramework<Repository.Database.dbContext>();
                //使用Dashboard，这是一个Cap的可视化管理界面；默认地址:http://localhost:端口/cap
                options.UseDashboard();
                options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

                options.DefaultGroupName = "default";   //默认组名称
                options.GroupNamePrefix = null; //全局组名称前缀
                options.TopicNamePrefix = null; //Topic 统一前缀
                options.Version = "v1";
                options.FailedRetryInterval = 60;   //失败时重试间隔
                options.ConsumerThreadCount = 1;    //消费者线程并行处理消息的线程数，当这个值大于1时，将不能保证消息执行的顺序
                options.FailedRetryCount = 10;  //失败时重试的最大次数
                options.FailedThresholdCallback = null; //重试阈值的失败回调
                options.SucceedMessageExpiredAfter = 24 * 3600; //成功消息的过期时间（秒）
            }).AddSubscribeFilter<CapSubscribeFilter>();
            services.ConfigServies(Configuration);
            //注册Swagger生成器，定义一个和多个Swagger 文档
            services.AddSwaggerGen(options =>
            {
                options.OperationFilter<SwaggerOperationFilter>();

                options.MapType<long>(() => new OpenApiSchema { Type = "string", Format = "long" });
                 
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(Startup).Assembly.GetName().Name}.xml"), true);

                //其他类库的注释文件
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"Models.xml"), true);
                 
                //开启 Swagger JWT 鉴权模块
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
                {
                    Description = "在下框中输入请求头中需要添加Jwt授权Token：Bearer Token",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    BearerFormat = "JWT",
                    Scheme = "Bearer"
                });
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                                }
                            },
                        new string[] { }
                    }
                });
            });

            //腾讯MiniLive服务注册
            MiniLive.AppId = "wxf164719d9baf8d83";
            MiniLive.AppSecret = "7c635fbff5974b3919826e0cdcf4c8c4";
            TencentService.Helper.RedisHelper.ConnectionString = Configuration.GetConnectionString("redisConnection");
            services.AddSingleton<IMiniLive, MiniLive>();
            services.AddControllers().AddControllersAsServices(); //控制器当做实例创建

            //IDSERVER 使用授权服务器 用于单点登录
            services.AddAuthentication(o =>
            {
                o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme; 
            }).AddJwtBearer("Bearer", o =>
              {
                  o.Audience = "WebApi";
                  o.Authority = Configuration["IdentityServerUrl"];
                  o.RequireHttpsMetadata = false;
                  o.TokenValidationParameters.RequireExpirationTime = true;
                  o.TokenValidationParameters.ValidateAudience = false;
              }); 
        }

        //向 Startup.Configure 方法添加中间件组件的顺序定义了针对请求调用这些组件的顺序，以及响应的相反顺序。 此顺序对于安全性、性能和功能至关重要。
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IApiVersionDescriptionProvider provider)
        { 
            //开启倒带模式运行多次读取HttpContext.Body中的内容
            app.Use(next => context =>
            {
                context.Request.EnableBuffering();
                return next(context);
            });
            if (env.IsDevelopment())
            {
                //app.UseDeveloperExceptionPage();
                ////注册全局异常处理机制
                app.UseExceptionHandler(builder => builder.Run(async context => await Web.Actions.GlobalError.ErrorEvent(context)));
            }
            else
            {
                ////注册全局异常处理机制
                app.UseExceptionHandler(builder => builder.Run(async context => await Web.Actions.GlobalError.ErrorEvent(context)));
            }
            //kevin初始化
            app.UseKevin(); 
            //注册HostingEnvironment
            Web.Libraries.Start.StartHostingEnvironment.Add(env);
            //启用中间件服务对swagger-ui，指定Swagger JSON端点
            app.UseSwaggerUI(options =>
            {
                foreach (var description in provider.ApiVersionDescriptions)
                {
                    options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
                }

                options.RoutePrefix = "swagger";
            });

        }

        ///// <summary>
        ///// autoFAC 服务注册   AutoFac支持方法和属性（可以自己控制注入 需要标记注入）注入 ServiceCollection只支持构造函数注入 需要在Program替换IOC容器  
        ///  Per Dependency Single Instance  Per Lifetime Scope
        /// <param name="containerBuilder"></param>
        ///// </summary>
        public void ConfigureContainer(ContainerBuilder containerBuilder)
        {
            #region 生命周期
            //InstancePerLifetimeScope：同一个Lifetime生成的对象是同一个实例 
            //SingleInstance：单例模式，每次调用，都会使用同一个实例化的对象；每次都用同一个对象； 
            //InstancePerDependency：默认模式，每次调用，都会重新实例化对象；每次请求都创建一个新的对象；
            #endregion

            containerBuilder.RegisterType<Service.Services.v1.UserService>().As<Service.Services.v1.IUserService>().PropertiesAutowired(new AutowiredPropertySelect()).InstancePerDependency();
            containerBuilder.RegisterType<dbContext>().As<dbContext>().PropertiesAutowired(new AutowiredPropertySelect()).InstancePerDependency();
            containerBuilder.RegisterType<CurrentUser>().As<ICurrentUser>().PropertiesAutowired(new AutowiredPropertySelect()).InstancePerDependency(); 
            //PropertiesAutowired指定属性注入  
            containerBuilder.RegisterAssemblyTypes().PropertiesAutowired(new AutowiredPropertySelect());
            containerBuilder.RegisterModule<ConfigureAutofac>();
            var controllerBaseType = typeof(ControllerBase);
            containerBuilder.RegisterAssemblyTypes(AppDomain.CurrentDomain.GetAssemblies())
                .Where(t => controllerBaseType.IsAssignableFrom(t) && t != controllerBaseType)
                .PropertiesAutowired();
        }
    }
}
