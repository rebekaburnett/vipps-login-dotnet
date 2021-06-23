﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using IdentityModel;
using IdentityModel.Client;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vipps.Login.Models;

namespace Vipps.Login
{
    public class VippsLoginService : IVippsLoginService
    {
        private const string NorwegianLanguageTag = "no";
        private static readonly HttpClient _httpClient = new HttpClient();
        public const string VippsTestApi = "https://apitest.vipps.no/";
        public const string VippsProdApi = "https://api.vipps.no/";

        public virtual VippsUserInfo GetVippsUserInfo(IIdentity identity)
        {
            return GetVippsUserInfo(identity as ClaimsIdentity);
        }

        public virtual VippsUserInfo GetVippsUserInfo(ClaimsIdentity identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (!IsVippsIdentity(identity))
            {
                return null;
            }

            var subjectClaim = identity.FindFirst(JwtClaimTypes.Subject) ??
                               identity.FindFirst(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(subjectClaim?.Value, out var subject))
            {
                return null;
            }

            return new VippsUserInfo
            {
                Sub = subject,
                BirthDate = ParseDate(identity.FindFirst(JwtClaimTypes.BirthDate) ??
                                      identity.FindFirst(ClaimTypes.DateOfBirth)),
                Email = ParseString(identity.FindFirst(JwtClaimTypes.Email) ??
                                    identity.FindFirst(ClaimTypes.Email)),
                EmailVerified = ParseBool(identity.FindFirst(JwtClaimTypes.EmailVerified)),
                FamilyName = ParseString(identity.FindFirst(JwtClaimTypes.FamilyName) ??
                                         identity.FindFirst(ClaimTypes.Surname)),
                GivenName = ParseString(identity.FindFirst(JwtClaimTypes.GivenName) ??
                                        identity.FindFirst(ClaimTypes.GivenName)),
                Name = ParseString(identity.FindFirst(JwtClaimTypes.Name) ?? identity.FindFirst(ClaimTypes.Name)),
                PhoneNumber = ParseString(identity.FindFirst(JwtClaimTypes.PhoneNumber) ??
                                          identity.FindFirst(ClaimTypes.HomePhone) ??
                                          identity.FindFirst(ClaimTypes.MobilePhone) ??
                                          identity.FindFirst(ClaimTypes.OtherPhone)),
                Nnin = identity.FindFirst(VippsClaimTypes.Nnin)?.Value,
                Addresses = GetVippsAddresses(identity)
            };
        }

        public virtual async Task<IEnumerable<Claim>> GetUserInfoClaims(
            string userInfoEndpoint,
            string accessToken)
        {
            var response = await _httpClient.GetUserInfoAsync(new UserInfoRequest
            {
                Address = userInfoEndpoint,
                Token = accessToken
            });
            if (response.IsError)
                throw new OpenIdConnectProtocolException(response.Error);
            return response.Claims;
        }

        public virtual bool IsVippsIdentity(IIdentity identity)
        {
            return IsVippsIdentity(identity as ClaimsIdentity);
        }

        public virtual bool IsVippsIdentity(ClaimsIdentity identity)
        {
            if (identity == null)
            {
                return false;
            }

            var issuer = identity.FindFirst(JwtClaimTypes.Issuer);
            var validIssuers = GetValidIssuers();
            if (issuer == null)
            {
                return false;
            }

            return validIssuers.Any(validIssuer => issuer.Value.StartsWith(validIssuer));
        }

        protected virtual IEnumerable<VippsAddress> GetVippsAddresses(ClaimsIdentity identity)
        {
            return identity
                .FindAll(JwtClaimTypes.Address)
                .Select(x => DeserializeAddress(x, true))
                .Union(
                    identity
                    .FindAll(VippsClaimTypes.OtherAddresses)
                    .Select(x => DeserializeAddress(x)))
                .Where(x => x != null)
                .ToList();
        }

        protected virtual IEnumerable<string> GetValidIssuers()
        {
            yield return VippsTestApi;
            yield return VippsProdApi;
            if (!string.IsNullOrWhiteSpace(VippsLoginConfig.Authority))
            {
                yield return VippsLoginConfig.Authority;
            }
        }

        protected virtual VippsAddress DeserializeAddress(Claim claim, bool isPreferred = false)
        {
            if (string.IsNullOrWhiteSpace(claim?.Value))
            {
                return null;
            }

            var addressObject = JObject.Parse(claim.Value);

            // Ignore if address type is empty
            var token = addressObject["address_type"];
            if(token is null || (token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.ToString())))
            {
                return null;
            }
            var address = addressObject.ToObject<VippsAddress>();
            address.IsPreferred = isPreferred;
            return address;
        }

        protected virtual string ParseString(Claim stringClaim)
        {
            return stringClaim?.Value;
        }

        protected virtual bool ParseBool(Claim boolClaim)
        {
            return bool.TryParse(boolClaim?.Value, out var verified) && verified;
        }

        protected virtual DateTime ParseDate(Claim dateClaim)
        {
            DateTime.TryParse(dateClaim?.Value,
                CultureInfo.GetCultureInfoByIetfLanguageTag(NorwegianLanguageTag), DateTimeStyles.None,
                out var date);
            return date;
        }
    }
}