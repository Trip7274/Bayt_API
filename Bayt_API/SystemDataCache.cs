namespace Bayt_API;

public class SystemDataCache
{
    public SystemDataCache()
    {
        ApiConfig.LastUpdated = DateTime.Now;
    }

    private StatsApi.CpuData? CachedCpuStats { get; set; }
    private List<GpuHandling.GpuData>? CachedGpuStats { get; set; }
    private StatsApi.MemoryData? CachedMemoryStats { get; set; }
    private List<DiskHandling.DiskData>? CachedWatchedDiskData { get; set; }

    // Unchanging Values

    private readonly StatsApi.GeneralSpecs _cachedGeneralSpecs = StatsApi.GetGeneralSpecs();
    public bool IsPrivileged { get; } = ShellMethods.RunShell("id", "-u").StandardOutput == "0";

    // Methods to get data, encapsulating the caching logic
    public StatsApi.GeneralSpecs GetGeneralSpecs()
    {
        return _cachedGeneralSpecs;
    }

    public StatsApi.CpuData GetCpuData()
    {
        CachedCpuStats = StatsApi.GetCpuData(CachedCpuStats);
        return CachedCpuStats;
    }

    public List<GpuHandling.GpuData> GetGpuData()
    {
        CachedGpuStats = GpuHandling.GetGpuDataList(CachedGpuStats);
        return CachedGpuStats;
    }

    public StatsApi.MemoryData GetMemoryData()
    {
        CachedMemoryStats = StatsApi.GetMemoryData(CachedMemoryStats);
        return CachedMemoryStats;
    }

    public List<DiskHandling.DiskData> GetDiskData()
    {
        // Pass the correct list of disks from the constructor
        var watchedMounts = ApiConfig.WatchedMountsConfigs.WatchedMounts;
        CachedWatchedDiskData = DiskHandling.GetDiskDatas(watchedMounts, CachedWatchedDiskData);
        return CachedWatchedDiskData;
    }
}