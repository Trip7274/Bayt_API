namespace Bayt_API;

/// <summary>
/// Cache object for system data. Use the <c>CheckXData()</c> methods to update the respective properties.
/// </summary>
public class SystemDataCache
{
    public SystemDataCache()
    {
        ApiConfig.LastUpdated = DateTime.Now;
    }

    /// <summary>
    /// General specs include generally very static and general info about the system.
    /// Essentially, if it takes at least a (system) restart to fully change it, and it's not inherent to Bayt, it's probably here.
    /// </summary>
    /// <remarks>
    /// This does not have a <c>CheckGeneralSpecsData()</c> method, as it's only checked once, the first time it's retreived.
    /// </remarks>
    public readonly StatsApi.GeneralSpecs CachedGeneralSpecs = StatsApi.GetGeneralSpecs();
    /// <summary>
    /// Contains data about the system's current CPU. Make sure this is updated using <see cref="CheckCpuData"/>
    /// </summary>
    public StatsApi.CpuData? CachedCpuStats { get; private set; }
    /// <summary>
    /// Contains data about the system's current GPU. Make sure this is updated using <see cref="CheckGpuData"/>
    /// </summary>
    public List<GpuHandling.GpuData>? CachedGpuStats { get; private set; }
    /// <summary>
    /// Contains data about the system's current RAM, such as free/used bytes. Make sure this is updated using <see cref="CheckMemoryData"/>
    /// </summary>
    public StatsApi.MemoryData? CachedMemoryStats { get; private set; }
    /// <summary>
    /// Contains a list of data about the user's watched mounts (from the configuration). Make sure this is updated using <see cref="CheckDiskData"/>
    /// </summary>
    public List<DiskHandling.DiskData>? CachedWatchedDiskData { get; private set; }


    // Methods to update data

    /// <summary>
    /// Updates the cached CPU data stored in the <see cref="CachedCpuStats"/> property
    /// with the latest info from the <see cref="StatsApi.GetCpuData"/> method.
    /// Make sure to invoke this as to not serve stale data.
    /// </summary>
    public void CheckCpuData()
    {
        CachedCpuStats = StatsApi.GetCpuData();
    }

    /// <summary>
    /// Updates the cached GPU data stored in the <see cref="CachedGpuStats"/> property
    /// with the latest info from the <see cref="GpuHandling.GetGpuDataList"/> method.
    /// Make sure to invoke this as to not serve stale data.
    /// </summary>
    public void CheckGpuData()
    {
        CachedGpuStats = GpuHandling.GetGpuDataList();
    }

    /// <summary>
    /// Updates the cached RAM data stored in the <see cref="CachedMemoryStats"/> property
    /// with the latest info from the <see cref="StatsApi.GetMemoryData"/> method.
    /// Make sure to invoke this as to not serve stale data.
    /// </summary>
    public void CheckMemoryData()
    {
        CachedMemoryStats = StatsApi.GetMemoryData();
    }

    /// <summary>
    /// Updates the cached mounts data stored in the <see cref="CachedWatchedDiskData"/> property
    /// with the latest info from the <see cref="DiskHandling.GetDiskDatas"/> method.
    /// Make sure to invoke this as to not serve stale data.
    /// </summary>
    public void CheckDiskData()
    {
        CachedWatchedDiskData = DiskHandling.GetDiskDatas();
    }
}