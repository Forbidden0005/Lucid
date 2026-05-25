namespace Lucid.Services.Distributed;

/// <summary>
/// Infers a device's operational role from hardware characteristics.
/// Deterministic — no user input required, no ML models.
/// Priority order: Laptop (battery) → Gaming → Workstation → MediaServer → AlwaysOnNode → Secondary.
/// </summary>
internal static class DeviceRoleClassifier
{
    public static DeviceRole Classify(bool hasBattery, bool hasDedicatedGpu, int cpuCores, long ramBytes)
    {
        // Battery present → must be a laptop regardless of specs
        if (hasBattery)
            return DeviceRole.Laptop;

        var ramGb = ramBytes / (1024.0 * 1024.0 * 1024.0);

        // Gaming desktop: dedicated GPU + significant RAM + multi-core CPU
        if (hasDedicatedGpu && ramGb >= 16 && cpuCores >= 6)
            return DeviceRole.GamingDesktop;

        // Workstation: high RAM and core count, no dedicated gaming GPU required
        if (ramGb >= 32 && cpuCores >= 8)
            return DeviceRole.Workstation;

        // Media server: moderate specs, no battery, no gaming GPU — common HTPC / NAS pattern
        if (!hasDedicatedGpu && ramGb >= 8 && cpuCores >= 4)
            return DeviceRole.MediaServer;

        // Always-on node: low-power device (mini PC, NUC, Raspberry Pi-style)
        if (ramGb <= 8 && cpuCores <= 4)
            return DeviceRole.AlwaysOnNode;

        // Catch-all: mid-range desktop that doesn't fit other profiles
        return DeviceRole.SecondaryDevice;
    }

    public static string RoleGlyph(DeviceRole role) => role switch
    {
        DeviceRole.GamingDesktop   => "",  // Game controller
        DeviceRole.Workstation     => "",  // CPU / Processor
        DeviceRole.MediaServer     => "",  // TV / Media
        DeviceRole.Laptop          => "",  // Laptop
        DeviceRole.SecondaryDevice => "",  // Desktop PC
        DeviceRole.AlwaysOnNode    => "",  // Server rack
        DeviceRole.HomeServer      => "",  // Storage
        _                          => "",
    };

    public static string RoleLabel(DeviceRole role) => role switch
    {
        DeviceRole.GamingDesktop   => "Gaming Desktop",
        DeviceRole.Workstation     => "Workstation",
        DeviceRole.MediaServer     => "Media Server",
        DeviceRole.Laptop          => "Laptop",
        DeviceRole.SecondaryDevice => "Secondary Device",
        DeviceRole.AlwaysOnNode    => "Always-On Node",
        DeviceRole.HomeServer      => "Home Server",
        _                          => "Unknown Device",
    };

    public static string RoleColor(DeviceRole role) => role switch
    {
        DeviceRole.GamingDesktop   => "#A855F7",
        DeviceRole.Workstation     => "#3B82F6",
        DeviceRole.MediaServer     => "#F59E0B",
        DeviceRole.Laptop          => "#10B981",
        DeviceRole.SecondaryDevice => "#6366F1",
        DeviceRole.AlwaysOnNode    => "#22C55E",
        DeviceRole.HomeServer      => "#EF4444",
        _                          => "#6B7280",
    };
}
