﻿using Common;
using Common.IO;
using IdentityModel.Client;
using kevin.Permission.Permisson.Attributes;
using kevin.Permission.Service;
using Kevin.Common.Kevin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Web.Actions; 

namespace kevin.Permission.Permission.Action
{
    public class IdentityVerification
    {
        /// <summary>
        /// 权限校验
        /// </summary>
        /// <param name="authorizationHandlerContext"></param>
        /// <returns></returns>
        public static bool Authorization(AuthorizationHandlerContext authorizationHandlerContext)
        {

            if (authorizationHandlerContext.User.Identity.IsAuthenticated)
            { 
                if (authorizationHandlerContext.Resource is HttpContext httpContext)
                {
                    IssueNewToken(httpContext); 
                    var ad = httpContext.GetControllerActionDescriptor();
                    var isSkip = ad.MethodInfo.IsDefined(typeof(SkipAuthorityAttribute), false) || ad.ControllerTypeInfo.IsDefined(typeof(SkipAuthorityAttribute), false);
                    if (isSkip == true)
                    { 
                        return true;
                    }
                    var area = "";
                    try
                    {
                        var myarea = ad.ControllerTypeInfo.CustomAttributes.Where(x => x.AttributeType == typeof(MyAreaAttribute)).ToList();
                        if (myarea.Count>0)
                        { 
                            area = ad.ControllerTypeInfo.CustomAttributes.Where(x => x.AttributeType == typeof(MyAreaAttribute)).ToList().LastOrDefault().ConstructorArguments[1].Value.ToString();
                        }
                    }
                    catch (Exception)
                    {
                         
                    } 
                     
                    var PermissionId = $"{area}.{ad.ControllerName}.{ad.ActionName}";

                    var isPublic = ad.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), false) || ad.ControllerTypeInfo.IsDefined(typeof(AllowAnonymousAttribute), false);

                    var isAllRights = ad.MethodInfo.IsDefined(typeof(AuthorizeAttribute), false) || ad.ControllerTypeInfo.IsDefined(typeof(AuthorizeAttribute), false);   
                    if (isPublic == true)
                    { 
                        return true;
                    }
                    if (isAllRights)
                    {
                        bool canAccess = httpContext.RequestServices.GetService<IKevinPermissionService>().IsAccess(PermissionId, httpContext.RequestServices.GetService<IHttpContextAccessor>());

                        return canAccess;
                    }

                    return false;
                } 
                return true;
            }
            else
            {
                return false;
            }


        }

        /// <summary>
        /// 签发新Token
        /// </summary>
        /// <param name="httpContext"></param>
        private static void IssueNewToken(HttpContext httpContext)
        {  
            var exp = Convert.ToInt64(JwtToken.GetClaims("exp", httpContext.RequestServices.GetService<IHttpContextAccessor>()));
            var exptime = Common.DateTimeHelper.UnixToTime(exp);
            if (exptime > DateTime.Now && exptime < DateTime.Now.AddMinutes(20))
            {
                try
                {
                    //获取刷新令牌
                    var RefreshToken = RedisHelper.StrGet(httpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", ""));
                    var Result =   RenewTokenAsync(RefreshToken);
                    if (!string.IsNullOrEmpty(Result.Result))
                    {
                        httpContext.Response.Headers.Add("NewToken", Result.Result);
                        httpContext.Response.Headers.Add("Access-Control-Expose-Headers", "NewToken");  //解决 Ionic 取不到 Header中的信息问题
                    }
                }
                catch (Exception)
                {
                }
            }

        }

        /// <summary>
        /// 刷新token
        /// </summary>
        /// <returns></returns>
        private static async Task<string> RenewTokenAsync(string token)
        {
            var clinet = new HttpClient();
            var disco = await clinet.GetDiscoveryDocumentAsync(Config.Get()["IdentityServerUrl"]);
            if (!disco.IsError)
            {
                var refreshToken = await clinet.RequestRefreshTokenAsync(new RefreshTokenRequest
                {
                    ClientId = Config.Get()["IdentityServerInfo:ClientId"],
                    Address = disco.TokenEndpoint,
                    ClientSecret = Config.Get()["IdentityServerInfo:ClientSecret"],
                    Scope = Config.Get()["IdentityServerInfo:Scope"],
                    GrantType = OpenIdConnectGrantTypes.RefreshToken,
                    RefreshToken = token
                });
                if (!refreshToken.IsError)
                {
                    //保存刷新令牌
                    RedisHelper.StrSet(refreshToken.AccessToken, refreshToken.RefreshToken, TimeSpan.FromDays(2));
                    return refreshToken.AccessToken;
                }
            }

            return "";
        }
    }
}
