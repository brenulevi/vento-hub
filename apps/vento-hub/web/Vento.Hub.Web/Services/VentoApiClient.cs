using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace Vento.Hub.Web.Services;

public sealed class VentoApiClient(HttpClient client)
{
    public async Task<IReadOnlyList<VentoApplication>> GetApplicationsAsync()
    {
        return await client.GetFromJsonAsync<List<VentoApplication>>("apps") ?? [];
    }

    public async Task<RegistrationResult> RegisterAsync(string username, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("auth/register", new { username, email, password });
        if (response.IsSuccessStatusCode)
            return RegistrationResult.Success;
        return response.StatusCode == HttpStatusCode.BadRequest
            ? RegistrationResult.InvalidAccount
            : RegistrationResult.Failed;
    }

    public async Task<VentoAccount?> LoginAsync(string identifier, string password)
    {
        using var response = await client.PostAsJsonAsync("auth/login", new { identifier, password });
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<AccessTokenResponse>();
        if (string.IsNullOrWhiteSpace(session?.AccessToken))
            throw new HttpRequestException("O serviço de contas não retornou um token.");

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "auth/me");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var profileResponse = await client.SendAsync(profileRequest);
        profileResponse.EnsureSuccessStatusCode();
        var account = await profileResponse.Content.ReadFromJsonAsync<VentoAccount>()
            ?? throw new HttpRequestException("O serviço de contas não retornou um perfil.");
        return account with { AccessToken = session.AccessToken };
    }

    public async Task<string> IssueDesktopCodeAsync(string accessToken, string challenge)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/desktop/code")
        {
            Content = JsonContent.Create(new { codeChallenge = challenge })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DesktopCodeResponse>();
        return result?.Code ?? throw new HttpRequestException("O serviço de contas não retornou um código.");
    }

    public async Task<bool> HasAccountImageAsync(string accessToken)
    {
        using var request = AuthorizedImageRequest(HttpMethod.Head, accessToken);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<VentoAccount?> GetProfileAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<VentoAccount>();
    }

    public async Task<IReadOnlyList<VentoRoomSummary>> GetRoomsAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<VentoRoomSummary>>() ?? [];
    }

    public async Task<MediaConnection?> GetMediaConnectionAsync(string accessToken, Guid roomId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"rooms/{roomId}/media-token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MediaConnection>();
    }

    public async Task<bool> JoinRoomAsync(string accessToken, Guid roomId, string inviteCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rooms/join")
        {
            Content = JsonContent.Create(new { inviteCode })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return false;
        var joined = await response.Content.ReadFromJsonAsync<JoinedRoom>();
        return joined?.Id == roomId;
    }

    public async Task<(byte[] Data, string ContentType)?> GetAccountImageAsync(string accessToken)
    {
        using var request = AuthorizedImageRequest(HttpMethod.Get, accessToken);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsByteArrayAsync(),
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public async Task<HttpStatusCode> UploadAccountImageAsync(string accessToken, Stream stream, string contentType)
    {
        using var request = AuthorizedImageRequest(HttpMethod.Put, accessToken);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    public async Task RemoveAccountImageAsync(string accessToken)
    {
        using var request = AuthorizedImageRequest(HttpMethod.Delete, accessToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static HttpRequestMessage AuthorizedImageRequest(HttpMethod method, string accessToken)
    {
        var request = new HttpRequestMessage(method, "account/image");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private sealed record AccessTokenResponse(string AccessToken);
    private sealed record DesktopCodeResponse(string Code);
    private sealed record JoinedRoom(Guid Id);
}

public enum RegistrationResult
{
    Success,
    InvalidAccount,
    Failed
}

public sealed record VentoApplication(string Id, string Name, string Description, string Status);
public sealed record VentoAccount(string Username, string Email, string AccessToken = "");
public sealed record VentoRoomSummary(Guid Id, string Name, bool IsOwner, int ParticipantCount, int OnlineCount);
public sealed record MediaConnection(string Url, string Token, string RoomName, bool CanPublish);
