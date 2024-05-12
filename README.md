# Configuration

```json
{
    // File Config
    "RootMachineDir": "C:\\tmp", // required - no default set    
    "MTSFileName" : "MacToServer.txt",
    "STMFileName" : "ServerToMac.txt",
    "CSVSeparator" : ",",

    // MQTT Config
    "Server" : "localhost",
    "Port": 1883,
    "ClientId" : "WatchPuppy",

    "RootTopic" : "macs",
    "OutTopic" : "out",
    "InTopic" : "in",

    // required - no default set
    // each index can only be used once
    "Format" : 
    {
        "lorem": 0,
        "ipsum": 1,
        "dolor": 3,
        "si": 2,
        "amet": 4
    }
}
```
