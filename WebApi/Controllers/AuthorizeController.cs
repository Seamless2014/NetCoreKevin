﻿ 
using Common;
using IdentityModel.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Models.Dtos;
using Repository.Database;
using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Web.Actions;
using WebApi.Controllers.Bases;

namespace WebApi.Controllers
{


    /// <summary>
    /// 系统访问授权模块
    /// </summary>
    [ApiVersionNeutral]
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AuthorizeController : PubilcControllerBase
    {

        public IConfiguration Configuration { get; set; }

        public AuthorizeController(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        /// <summary>
        /// GetErr
        /// </summary> 
        /// <returns></returns>
        [HttpPost("GetErr")]
        public string GetErr()
        {
            int i = int.Parse("ABD");
            return i.ToString();

        }
        ///// <summary>
        ///// 获取Token认证信息
        ///// </summary>
        ///// <param name="login">登录信息集合</param>
        ///// <returns></returns>
        //[HttpPost("GetToken")]
        //public string GetToken([FromBody] dtoLogin login)
        //{


        //    var user = db.TUser.Where(t => (t.Name == login.Name || t.Phone == login.Name || t.Email == login.Name) && t.PassWord == login.PassWord).FirstOrDefault();

        //    if (user != null)
        //    {
        //        TUserToken userToken = new TUserToken();
        //        userToken.Id = Guid.NewGuid();
        //        userToken.UserId = user.Id;
        //        userToken.CreateTime = DateTime.Now;

        //        db.TUserToken.Add(userToken);
        //        db.SaveChanges();

        //        var claim = new Claim[]{
        //                new Claim("tokenId",userToken.Id.ToString()),
        //                     new Claim("userId",user.Id.ToString())
        //                };


        //        var ret = Web.Libraries.Verify.JwtToken.GetToken(claim);

        //        return ret;
        //    }
        //    else
        //    {

        //        HttpContext.Response.StatusCode = 400;

        //        HttpContext.Items.Add("errMsg", "Authorize.GetToken.'Wrong user name or password'");

        //        return "";
        //    }

        //}
        ///// <summary>
        ///// 获取Token认证信息
        ///// </summary>
        ///// <param name="login">登录信息集合</param>
        ///// <returns></returns>
        [HttpPost("GetToken")]
        public async Task<string> GetToken([FromBody] dtoLogin login)
        {
            var clinet = new HttpClient();
            var disco = await clinet.GetDiscoveryDocumentAsync(Configuration["IdentityServerUrl"]);
            if (disco.IsError)
            {
                ResponseErrAction.ExceptionMessage("登录异常");
                return default;
            }

            var tokenResponse = await clinet.RequestPasswordTokenAsync(new PasswordTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = "UserClient",
                ClientSecret = "UserClientSecrets",
                Scope = "WebApi offline_access profile openid",
                UserName = login.Name,
                Password = login.PassWord,
            });
            if (tokenResponse.IsError)
            {
                ResponseErrAction.ExceptionMessage(tokenResponse.ErrorDescription);
                return default;
            }
            //保存刷新令牌
            RedisHelper.StrSet(tokenResponse.AccessToken, tokenResponse.RefreshToken, TimeSpan.FromDays(2));
            return tokenResponse.AccessToken;
        }



        /// <summary>
        /// 通过微信小程序Code获取Token认证信息
        /// </summary>
        /// <param name="keyValue">key 为weixinkeyid, value 为 code</param>
        /// <returns></returns>
        [HttpPost("GetTokenByWeiXinMiniAppCode")]
        public async Task<string> GetTokenByWeiXinMiniAppCode([FromBody] dtoKeyValue keyValue)
        {
              
            var weixinkeyid = Guid.Parse(keyValue.Key.ToString());
            string code = keyValue.Value.ToString();

            var weixinkey = db.TWeiXinKey.Where(t => t.Id == weixinkeyid).FirstOrDefault();

            var weiXinHelper = new Web.Libraries.WeiXin.MiniApp.WeiXinHelper(weixinkey.WxAppId, weixinkey.WxAppSecret);


            var wxinfo = weiXinHelper.GetOpenIdAndSessionKey(code);

            string openid = wxinfo.openid;
            string sessionkey = wxinfo.sessionkey;

            var user = db.TUserBindWeixin.Where(t => t.WeiXinOpenId == openid).Select(t => t.User).FirstOrDefault();


            if (user == null)
            {

                bool isAction = false;

                while (isAction == false)
                {
                    if (Common.RedisHelper.Lock("GetTokenByWeiXinMiniAppCode" + openid, "123456", TimeSpan.FromSeconds(5)))
                    {
                        isAction = true;

                        user = db.TUserBindWeixin.Where(t => t.WeiXinOpenId == openid).Select(t => t.User).FirstOrDefault();

                        if (user == null)
                        {
                            //注册一个只有基本信息的账户出来
                            user = new TUser();

                            user.Id = Guid.NewGuid();
                            user.IsDelete = false;
                            user.CreateTime = DateTime.Now;
                            user.Name = DateTime.Now.ToString() + "微信小程序新用户";
                            user.NickName = user.Name;
                            user.PassWord = Guid.NewGuid().ToString();

                            //开发时记得调整这个值
                            user.RoleId = default;

                            db.TUser.Add(user);

                            db.SaveChanges();

                            TUserBindWeixin userBind = new TUserBindWeixin();
                            userBind.Id = Guid.NewGuid();
                            userBind.IsDelete = false;
                            userBind.CreateTime = DateTime.Now;
                            userBind.UserId = user.Id;
                            userBind.WeiXinKeyId = weixinkeyid;
                            userBind.WeiXinOpenId = openid;

                            db.TUserBindWeixin.Add(userBind);

                            db.SaveChanges();
                        }

                        Common.RedisHelper.UnLock("GetTokenByWeiXinMiniAppCode" + openid, "123456");
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }

            }
            var clinet = new HttpClient();
            var disco = await clinet.GetDiscoveryDocumentAsync(Configuration["IdentityServerUrl"]);
            if (disco.IsError)
            {
                ResponseErrAction.ExceptionMessage("登录异常");
                return default;
            }

            var tokenResponse = await clinet.RequestPasswordTokenAsync(new PasswordTokenRequest
            {
                Address = disco.TokenEndpoint,
                ClientId = "UMUserClient",
                ClientSecret = "UMUserClientSecrets",
                Scope = "WebApi offline_access profile openid",
                UserName = user.Id.ToString(),
                Password = user.Id.ToString(),
            });
            if (tokenResponse.IsError)
            {
                ResponseErrAction.ExceptionMessage(tokenResponse.ErrorDescription);
                return default;
            }
            //保存刷新令牌
            RedisHelper.StrSet(tokenResponse.AccessToken, tokenResponse.RefreshToken, TimeSpan.FromDays(2));
            return tokenResponse.AccessToken; 
        }




        /// <summary>
        /// 利用手机号和短信验证码获取Token认证信息
        /// </summary>
        /// <param name="keyValue">key 为手机号，value 为验证码</param>
        /// <returns></returns>
        [HttpPost("GetTokenBySms")]
        public async Task<string> GetTokenBySms(dtoKeyValue keyValue)
        {
            if (Web.Auth.AuthorizeAction.SmsVerifyPhone(keyValue))
            {
                string phone = keyValue.Key.ToString();


                var user = db.TUser.Where(t => t.IsDelete == false && (t.Name == phone || t.Phone == phone) && t.RoleId == default).FirstOrDefault();

                if (user == null)
                {
                    //注册一个只有基本信息的账户出来

                    user = new TUser();

                    user.Id = Guid.NewGuid();
                    user.IsDelete = false;
                    user.CreateTime = DateTime.Now;
                    user.Name = DateTime.Now.ToString() + "手机短信新用户";
                    user.NickName = user.Name;
                    user.PassWord = Guid.NewGuid().ToString();
                    user.Phone = phone;

                    db.TUser.Add(user);

                    db.SaveChanges();
                }

                return await GetToken(new dtoLogin { Name = user.Name, PassWord = user.PassWord });
            }
            else
            {
                HttpContext.Response.StatusCode = 400;

                HttpContext.Items.Add("errMsg", "Authorize.GetTokenBySms.'New password is not allowed to be empty'");

                return "";
            }

        }




        /// <summary>
        /// 发送短信验证手机号码所有权
        /// </summary>
        /// <param name="keyValue">key 为手机号，value 可为空</param>
        /// <returns></returns>
        [HttpPost("SendSmsVerifyPhone")]
        public bool SendSmsVerifyPhone(dtoKeyValue keyValue)
        {

            string phone = keyValue.Key.ToString();

            string key = "VerifyPhone_" + phone;

            if (Common.RedisHelper.IsContainStr(key) == false)
            {

                Random ran = new Random();
                string code = ran.Next(100000, 999999).ToString();

                var jsonCode = new
                {
                    code = code
                };

                Common.AliYun.SmsHelper sms = new Common.AliYun.SmsHelper();
                var smsStatus = sms.SendSms(phone, "短信模板编号", "短信签名", Common.Json.JsonHelper.ObjectToJSON(jsonCode));

                if (smsStatus)
                {
                    Common.RedisHelper.StrSet(key, code, new TimeSpan(0, 0, 5, 0));

                    return true;
                }
                else
                {
                    return false;
                }

            }
            else
            {
                return false;
            }

        }



        /// <summary>
        /// 通过微信APP Code获取Token认证信息
        /// </summary>
        /// <param name="keyValue">key 为weixinkeyid, value 为 code</param>
        /// <returns></returns>
        [HttpPost("GetTokenByWeiXinAppCode")]
        public async Task<string> GetTokenByWeiXinAppCode(dtoKeyValue keyValue)
        {



            var weixinkeyid = Guid.Parse(keyValue.Key.ToString());
            string code = keyValue.Value.ToString();


            var wxInfo = db.TWeiXinKey.Where(t => t.Id == weixinkeyid).FirstOrDefault();

            var weiXinHelper = new Web.Libraries.WeiXin.App.WeiXinHelper(wxInfo.WxAppId, wxInfo.WxAppSecret);

            var accseetoken = weiXinHelper.GetAccessToken(code).accessToken;

            var openid = weiXinHelper.GetAccessToken(code).openId;

            var userInfo = weiXinHelper.GetUserInfo(accseetoken, openid);

            var user = db.TUserBindWeixin.Where(t => t.IsDelete == false && t.WeiXinKeyId == weixinkeyid && t.WeiXinOpenId == userInfo.openid).Select(t => t.User).FirstOrDefault();

            if (user == null)
            {
                user = new TUser();
                user.Id = Guid.NewGuid();
                user.IsDelete = false;
                user.CreateTime = DateTime.Now;

                user.Name = userInfo.nickname;
                user.NickName = user.Name;
                user.PassWord = Guid.NewGuid().ToString();

                db.TUser.Add(user);
                db.SaveChanges();

                var bind = new TUserBindWeixin();
                bind.Id = Guid.NewGuid();
                bind.IsDelete = false;
                bind.CreateTime = DateTime.Now;

                bind.WeiXinKeyId = weixinkeyid;
                bind.UserId = user.Id;
                bind.WeiXinOpenId = openid;

                db.TUserBindWeixin.Add(bind);

                db.SaveChanges();
            }

            return await GetToken(new dtoLogin { Name = user.Name, PassWord = user.PassWord });

        }



    }
}
