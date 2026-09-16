namespace Booking.Service.IntegrationTests;

internal static class DockerRequirement
{
    private const string Message = "Docker is unavailable on this host; Booking integration tests did not execute.";

    public static void SkipIfUnavailable(bool isDockerAvailable)
    {
        Assert.True(isDockerAvailable, Message);
    }
}