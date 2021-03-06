﻿namespace AccidentalFish.ApplicationSupport.Core.Configuration
{
    public interface IConfiguration
    {
        string StorageAccountConnectionString { get; }
        string SqlConnectionString { get; }
        string ServiceBusConnectionString { get; }
        string this[string key] { get; }
    }
}
