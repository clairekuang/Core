﻿using System;
using System.Threading.Tasks;
using GraphQL.Client.Http;
using GraphQL;
using GraphQL.Client.Serializer.Newtonsoft;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Speckle.Core.Logging;

namespace Speckle.Core.Credentials
{

  public class Account : IEquatable<Account>
  {

    public string id
    {
      get
      {
        if (serverInfo == null || userInfo == null)
          Log.CaptureAndThrow(new SpeckleException("Incomplete account info: cannot generate id."));
        return Speckle.Core.Models.Utilities.hashString(serverInfo.url + userInfo.email);
      }
    }

    public string token { get; set; }

    public string refreshToken { get; set; }

    public bool isDefault { get; set; } = false;

    public ServerInfo serverInfo { get; set; }

    public UserInfo userInfo { get; set; }

    public Account() { }

    #region public methods
    public async Task<UserInfo> Validate()
    {
      using var httpClient = new HttpClient();

      httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

      using var gqlClient = new GraphQLHttpClient(new GraphQLHttpClientOptions() { EndPoint = new Uri(new Uri(serverInfo.url), "/graphql") }, new NewtonsoftJsonSerializer(), httpClient);

      var request = new GraphQLRequest
      {
        Query = @" query { user { name email id company } }"
      };

      var response = await gqlClient.SendQueryAsync<UserInfoResponse>(request);

      if (response.Errors != null)
        return null;

      return response.Data.user;
    }

    public async Task RotateToken()
    {
      using (var client = new HttpClient())
      {
        var request = new HttpRequestMessage()
        {
          RequestUri = new Uri(new Uri(serverInfo.url), "auth/token"),
          Method = HttpMethod.Post
        };

        var payload = new
        {
          appId = AccountManager.APPID,
          appSecret = AccountManager.SECRET,
          refreshToken
        };

        request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload));

        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var _response = await client.SendAsync(request);

        try
        {
          _response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
          Log.CaptureAndThrow(new SpeckleException($"Failed to rotate token. Response status: {_response.StatusCode}.", e));
        }

        var _tokens = JsonConvert.DeserializeObject<TokenExchangeResponse>(await _response.Content.ReadAsStringAsync());

        refreshToken = _tokens.refreshToken;
        token = _tokens.token;

        AccountManager.UpdateOrSaveAccount(this);
      }
    }

    public bool Equals(Account other)
    {
      return other.userInfo.email == userInfo.email && other.serverInfo.url == serverInfo.url;
    }

    public override string ToString() 
    {
      return userInfo.email + " - " + serverInfo.name;
    }

    #endregion
  }
}
