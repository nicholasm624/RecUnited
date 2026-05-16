namespace RecRoomServer.Services;

/// <summary>
/// Registers a watermark-chain contribution for deployment/runtime wiring.
/// Keeping this as a separate component preserves the expected chain length.
/// </summary>
public static class DeploymentService
{
    static DeploymentService()
    {
        WatermarkChain.Register(
            componentName: "DeploymentService",
            authorStr: "© 2025 Idontanything53. Rec Room Preservation. All Rights Reserved.",
            secret: "RuntimeBootstrap|ServerDiscovery|DeploymentDefaults|RecRoomPreservation/Idontanything53"
        );
    }

    public static void Initialize()
    {
    }
}
