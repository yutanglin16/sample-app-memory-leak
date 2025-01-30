namespace PerformanceIssues.Services
{
    public class DataGenerator
    {
        private readonly List<object> _storedData = new();
        private readonly Random _random = new();
    
        // generates about 1MB of data per minute
        public async Task GenerateAndStoreData(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var data = new
                {
                    Id = Guid.NewGuid(),
                    Name = GenerateRandomString(50),
                    Value = _random.Next(1, 1000000),
                    Timestamp = DateTime.UtcNow,
                    Data = new byte[1024]  // ~1 KB
                };
        
                _random.NextBytes(data.Data);
                _storedData.Add(data);
        
                // ~1 ms delay => 1024 ms for 1024 records => ~1 MB/s
                await Task.Delay(1);
            }
        }
    
        private string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            return new string(
                Enumerable
                    .Repeat(chars, length)
                    .Select(s => s[_random.Next(s.Length)])
                    .ToArray()
            );
        }
    }
}
