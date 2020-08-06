# Vipps Log In OIDC Authentication middleware for ASP.NET and Episerver

## Description

This repository contains the code to use Vipps Log In OpenIdConnect (OIDC) Authentication middleware in your ASP.NET application using OWIN. Information about the Vipps Log In API can be found here: https://github.com/vippsas/vipps-login-api

This repository consists of three NuGet packages:

- Vipps.Login - OWIN Middleware that enables an application to use OpenIdConnect for authentication.
- Vipps.Login.Episerver - Episerver code for Vipps Login
- Vipps.Login.Episerver.Commerce - Episerver Commerce code for Vipps Login

## Features

- OWIN Middleware to support Vipps Login OpenIdConnect
- Library to simplify configuration and set up

## How to get started?

Start by installing the NuGet packages:

For the OWIN middleware

- `Install-Package Vipps.Login`

And for the Episerver extensions

- `Install-Package Vipps.Login.Episerver`
- `Install-Package Vipps.Login.Episerver.Commerce`

### Get API keys for Vipps Log In API

Activate and set up Vipps Login: https://github.com/vippsas/vipps-login-api/blob/master/vipps-login-api-faq.md#how-can-i-activate-and-set-up-vipps-login

Configure a redirect URI to your site(s): `https://{your-site}/vipps-login` (fill in the correct url there, it can be localhost as well)

Add the ClientId and the ClientSecret to the AppSettings, as such:

```
<add key="VippsLogin:ClientId" value="..." />
<add key="VippsLogin:ClientSecret" value="..." />
<add key="VippsLogin:Authority" value="https://apitest.vipps.no/access-management-1.0/access" />
```

For production use

```
<add key="VippsLogin:Authority" value="https://api.vipps.no/access-management-1.0/access" />
```

See https://github.com/vippsas/vipps-login-api/blob/master/vipps-login-api.md#base-urls

### Prepare Episerver for OpenID Connect

Described in detail here: https://world.episerver.com/documentation/developer-guides/CMS/security/integrate-azure-ad-using-openid-connect/

#### 1. Disable Role and Membership Providers

```
<authentication mode="None" />
<membership>
  <providers>
    <clear/>
  </providers>
</membership>
<roleManager enabled="false">
  <providers>
    <clear/>
  </providers>
</roleManager>
```

#### 2. Configure Episerver to support claims

```
<episerver.framework>
  <securityEntity>
    <providers>
      <add name="SynchronizingProvider"
           type="EPiServer.Security.SynchronizingRolesSecurityEntityProvider, EPiServer"/>
    </providers>
  </securityEntity>
  <virtualRoles addClaims="true">
     //existing virtual roles
  </virtualRoles>
```

#### 3. Configure Vipps OIDC during app Startup

Here you can find the default configuration needed to support Vipps OIDC. Some tips:

1. Be sure to configure only the scopes you actually need.
2. If authentication fails, we suggest redirecting to the normal login page and show an informational message.
3. Determine what you which information from Vipps you want to sync. By default we will update the customer contact and the customer addresses upon login.

```csharp
public class Startup
{
    public void Configuration(IAppBuilder app)
    {
        // Enable the application to use a cookie to store information for the signed in user
        app.UseCookieAuthentication(new CookieAuthenticationOptions
        {
            AuthenticationType = DefaultAuthenticationTypes.ApplicationCookie,
            LoginPath = new PathString("/util/login.aspx")
        });

        // Vipps OIDC configuration starts here
        // This should match CookieAuthentication AuthenticationType above ^
        app.SetDefaultSignInAsAuthenticationType(DefaultAuthenticationTypes.ApplicationCookie);
        app.UseOpenIdConnectAuthentication(new VippsOpenIdConnectAuthenticationOptions(
            VippsLoginConfig.ClientId,
            VippsLoginConfig.ClientSecret,
            VippsLoginConfig.Authority
            )
        {
            // 1. Here you pass in the scopes you need
            Scope = string.Join(" ", new []
            {
                VippsScopes.OpenId,
                VippsScopes.Email,
                VippsScopes.Name,
                VippsScopes.BirthDate,
                VippsScopes.Address,
                VippsScopes.PhoneNumber
            }),
            // Various notifications that we can handle during the auth flow
            // By default it will handle:
            // RedirectToIdentityProvider - Redirecting to Vipps using correct RedirectUri
            // AuthorizationCodeReceived - Exchange Authentication code for id_token and access_token
            // DefaultSecurityTokenValidated - Find matching CustomerContact

            Notifications = new VippsEpiNotifications
            {
                AuthenticationFailed = context =>
                {
                    _logger.Error("Vipps.Login failed", context.Exception);

                    var message = "Something went wrong. Please contact customer support.";
                    switch (context.Exception)
                    {
                        case VippsLoginDuplicateAccountException _:
                            message = "Multiple accounts found matching this Vipps user info. Please log in and link your Vipps account through the profile page.";
                            break;
                        case VippsLoginSanityCheckException _:
                            message = "Existing account found but did not pass Vipps sanity check. Please log in and link your Vipps account through the profile page.";
                            break;
                    }

                    // 2. Redirect to login page and display message
                    context.HandleResponse();
                    context.Response.Redirect($"/user?error={message}");
                    return (Task)Task.FromResult<int>(0);
                }
            }
        });
        // Trigger Vipps middleware on this path to start authentication
        app.Map("/vipps-login", map => map.Run(ctx =>
        {
            var service = ServiceLocator.Current.GetInstance<IVippsLoginCommerceService>();

            // 3. Vipps log in and sync Vipps user info
            if (service.HandleLogin(ctx, new VippsSyncOptions
            {
                SyncContactInfo = true,
                SyncAddresses = true
            })) return Task.Delay(0);

            // Link Vipps account to current logged in user account
            bool.TryParse(ctx.Request.Query.Get("LinkAccount"), out var linkAccount);
            if (linkAccount && service.HandleLinkAccount(ctx)) return Task.Delay(0);

            // Return to this url after authenticating
            var returnUrl = ctx.Request.Query.Get("ReturnUrl") ?? "/";
            service.HandleRedirect(ctx, returnUrl);

            return Task.Delay(0);
        }));
        app.Map("/vipps-logout", map =>
        {
            map.Run(context =>
            {
                context.Authentication.SignOut(VippsAuthenticationDefaults.AuthenticationType);
                return Task.FromResult(0);
            });
        });

        // Required for AntiForgery to work
        // Otherwise it'll throw an exception about missing claims
        AntiForgeryConfig.UniqueClaimTypeIdentifier = ClaimTypes.Name;
    }
}
```

When the user goes to `https://{your-site}/vipps-login`, the Vipps middleware will be triggered and it will redirect the user to the Vipps log in environment. You will have to configure this redirect URL in Vipps, as described here: https://github.com/vippsas/vipps-login-api/blob/master/vipps-login-api-faq.md#how-can-i-activate-and-set-up-vipps-login

You can add a ReturnUrl to redirect the user once they are logged in, for example `https://{your-site}/vipps-login?ReturnUrl=/vipps-landing`.

Vipps is using the OpenIdConnect Authorization Code Grant flow, this means the user is redirected back to your environment with a Authorization token. The middleware will validate the token and exchange it for an `id_token` and an `access_token`. A `ClaimsIdentity` will be created which will contain the information of the scopes that you configured (email, name, addresses etc).

### Link Vipps to an existing account

If you want to allow **logged in users** to link to Vipps to their existing non Vipps account, you can add a link the redirect them to `https://{your-site}/vipps-login?LinkAccount=true`. When they visit that link, they will be redirected to Vipps and can go through the log in process. Once they're redirected back to your site, their Vipps account will be linked to their existing account. This means that they will now be able to use Vipps to access their existing account and they can sync their data from Vipps to Episerver.

### Customized 'sanity check' during login

If the user tries to log in with Vipps and there is an existing account that matches the Vipps information (email or phone number), the library will execute a 'sanity check'. This is done to make sure that the account is not an old account where the user has abandoned the phone number or e-mail address an this has been picked up by someone else at a later time.
By default it will compare the first name and the last name, however it is easy to change this behaviour by implementing a custom sanity check and registering it in the DI container:

```csharp
public class VippsLoginSanityCheck : IVippsLoginSanityCheck
{
    public bool IsValidContact(CustomerContact contact, VippsUserInfo userInfo)
    {
        // your logic here
    }
}
```

### Linking a Vipps account to multiple webshop accounts

It is not possible to link a Vipps account to multiple accounts on the webshop. The library will throw a ` VippsLoginLinkAccountException` with the `UserError` property set to true. To recover from this, you can give the user the option to remove the link between the webshop account and the Vipps account. You can use the `IVippsLoginCommerceService.RemoveLinkToVippsAccount(CustomerContact contact)` method to remove the link to the existing account.

### Accessing Vipps user data

The Vipps UserInfo can be accessed by calling `IVippsLoginService.GetVippsUserInfo(IIdentity identity)`, this will give you the user info that was retrieved when the user logged in (cached).

### Syncing Vipps user data

By default the Vipps user info and the Vipps addresses will be synced during log in. If decide not to sync this data during log in, you might want to sync the data later on.
To do so you can call `IVippsLoginCommerceService.SyncInfo` and use the `VippsSyncOptions` parameter to configure what to sync:

```csharp
public class VippsPageController : PageController<VippsPage>
{
    private readonly IVippsLoginCommerceService _vippsLoginCommerceService;
    private readonly CustomerContext _customerContext;
    public VippsPageController(IVippsLoginCommerceService vippsLoginCommerceService, CustomerContext customerContext)
    {
        _vippsLoginCommerceService = vippsLoginCommerceService;
        _customerContext = customerContext;
    }

    public ActionResult Index(VippsPage currentPage)
    {
        // Sync user info and addresses
        _vippsLoginCommerceService.SyncInfo(
            User.Identity,
            _customerContext.CurrentContact,
            new VippsSyncOptions {
                SyncContactInfo = true, SyncAddresses = true
            }
        );

        return View();
    }
}
```

## More info

- https://github.com/vippsas/vipps-login-api
- https://github.com/vippsas/vipps-developers
- https://openid.net/specs/openid-connect-core-1_0.html#CodeFlowAuth
- https://world.episerver.com/documentation/developer-guides/commerce/security/support-for-openid-connect-in-episerver-commerce/

## Package maintainer

https://github.com/brianweet

## Changelog

[Changelog](CHANGELOG.md)
