﻿using System;
using System.Web;
using NewLife.Cube.Entity;
using NewLife.Model;
using NewLife.Security;
using NewLife.Web;
using XCode;
using XCode.Membership;

namespace NewLife.Cube.Web
{
    /// <summary>单点登录提供者</summary>
    public class SsoProvider
    {
        #region 属性
        /// <summary>用户管理提供者</summary>
        public IManageProvider Provider { get; set; }

        /// <summary>重定向地址。~/Sso/LoginInfo</summary>
        public String RedirectUrl { get; set; }

        /// <summary>登录成功后跳转地址。~/Admin</summary>
        public String SuccessUrl { get; set; }

        /// <summary>本地登录检查地址。~/Admin/User/Login</summary>
        public String LoginUrl { get; set; }

        /// <summary>已登录用户</summary>
        public IManageUser Current => Provider.Current;
        #endregion

        #region 构造
        /// <summary>实例化</summary>
        public SsoProvider()
        {
            Provider = ManageProvider.Provider;
            RedirectUrl = "~/Sso/LoginInfo";
            SuccessUrl = "~/Admin";
            LoginUrl = "~/Admin/User/Login";
        }
        #endregion

        #region 方法
        /// <summary>获取OAuth客户端</summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public virtual OAuthClient GetClient(String name) => OAuthClient.Create(name);

        /// <summary>获取返回地址</summary>
        /// <param name="request">请求对象</param>
        /// <param name="referr">是否使用引用</param>
        /// <returns></returns>
        public virtual String GetReturnUrl(HttpRequestBase request, Boolean referr)
        {
            var url = request["r"];
            if (url.IsNullOrEmpty() && referr) url = request.UrlReferrer + "";
            if (!url.IsNullOrEmpty() && url.StartsWithIgnoreCase("http"))
            {
                var baseUri = request.GetRawUrl();

                var uri = new Uri(url);
                if (uri != null && uri.Host.EqualIgnoreCase(baseUri.Host)) url = uri.PathAndQuery;
            }

            return url;
        }

        /// <summary>获取回调地址</summary>
        /// <param name="request"></param>
        /// <param name="returnUrl"></param>
        /// <returns></returns>
        public virtual String GetRedirect(HttpRequestBase request, String returnUrl = null)
        {
            if (returnUrl.IsNullOrEmpty()) returnUrl = request["r"];

            var uri = RedirectUrl.AsUri(request.GetRawUrl()) + "";

            return uri.AppendReturn(returnUrl);
        }

        /// <summary>登录成功</summary>
        /// <param name="client">OAuth客户端</param>
        /// <param name="service">服务提供者。可用于获取HttpContext成员</param>
        /// <returns></returns>
        public virtual String OnLogin(OAuthClient client, IServiceProvider service)
        {
            var openid = client.OpenID;
            if (openid.IsNullOrEmpty()) openid = client.UserName;

            // 根据OpenID找到用户绑定信息
            var uc = UserConnect.FindByProviderAndOpenID(client.Name, openid);
            if (uc == null) uc = new UserConnect { Provider = client.Name, OpenID = openid };

            uc.Fill(client);

            // 强行绑定，把第三方账号强行绑定到当前已登录账号
            var forceBind = false;
            var req = service.GetService<HttpRequest>();
            if (req != null) forceBind = req["sso_action"].EqualIgnoreCase("bind");

            // 检查绑定
            var user = Provider.FindByID(uc.UserID);
            if (forceBind || user == null || !uc.Enable) user = OnBind(uc, client);

            // 填充昵称等数据
            Fill(client, user);

            if (user is IAuthUser user3) user3.Save();
            uc.Save();

            if (!user.Enable) throw new InvalidOperationException("用户已禁用！");

            // 登录成功，保存当前用户
            Provider.Current = user;

            return SuccessUrl;
        }

        /// <summary>填充用户</summary>
        /// <param name="client"></param>
        /// <param name="user"></param>
        protected virtual void Fill(OAuthClient client, IManageUser user)
        {
            if (user.Name.IsNullOrEmpty()) user.Name = client.UserName ?? client.OpenID;
            if (user.NickName.IsNullOrEmpty()) user.NickName = client.NickName;

            var dic = client.Items;
            // 邮箱
            if (dic != null && user is UserX user2)
            {
                if (user2.Mail.IsNullOrEmpty() && dic.TryGetValue("email", out var email)) user2.Mail = email;
            }
            // 头像
            if (dic != null && user is IEntity entity)
            {
                if ((entity["Avatar"] + "").IsNullOrEmpty()) entity.SetItem("Avatar", client.Avatar);
            }
        }

        /// <summary>绑定用户</summary>
        /// <param name="uc"></param>
        /// <param name="client"></param>
        public virtual IManageUser OnBind(UserConnect uc, OAuthClient client)
        {
            var prv = Provider;

            // 如果未登录，需要注册一个
            var user = prv.Current;
            if (user == null)
            {
                var set = Setting.Current;
                if (!set.AutoRegister) throw new InvalidOperationException("绑定要求本地已登录！");

                // 先找用户名，如果存在，就加上提供者前缀，直接覆盖
                var name = client.UserName;
                if (!name.IsNullOrEmpty())
                {
                    user = prv.FindByName(name);
                    if (user != null)
                    {
                        name = client.Name + "_" + name;
                        user = prv.FindByName(name);
                    }
                }
                else
                // QQ、微信 等不返回用户名
                {
                    // OpenID和AccessToken不可能同时为空
                    var openid = client.OpenID;
                    if (openid.IsNullOrEmpty()) openid = client.AccessToken;

                    // 过长，需要随机一个较短的
                    var num = openid.GetBytes().Crc();

                    name = client.Name + "_" + num.ToString("X8");
                    user = prv.FindByName(name);
                }

                if (user == null)
                {
                    // 新注册用户采用魔方默认角色
                    var rid = set.DefaultRole;

                    // 注册用户，随机密码
                    user = prv.Register(name, Rand.NextString(16), rid, true);
                }
            }

            uc.UserID = user.ID;
            uc.Enable = true;

            return user;
        }

        /// <summary>注销</summary>
        /// <returns></returns>
        public virtual void Logout()
        {
            Provider.Current = null;
        }
        #endregion
    }
}