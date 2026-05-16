namespace RecRoomServer.Models;

public record AccountModel(
    long accountId,
    string username,
    string displayName,
    string profileImage,
    string bannerImage,
    bool isJunior,
    string platform,
    int level,
    int xp,
    bool isModerator,
    bool isMonetized,
    string createdAt,
    string bio,
    int subscriberCount,
    bool isSubscribed
);

public record TokenResponse(
    string access_token,
    string token_type,
    long expires_in,
    string refresh_token,
    string scope
);

public record JoinRoomResponse(
    string photonRoomId,
    string photonRegionId,
    string photonAppId,
    string masterServer
);

public record ProgressModel(
    int level,
    int xp,
    int xpForNextLevel
);
