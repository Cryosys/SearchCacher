# SearchCacher
 Web app to search on cached storage. Usefull for large storage clusters.

 The app allows to search on local/external or network based storage on near realtime data. The results will be available in just a few seconds at most, the time highly depends on 
 the complexity of the search and the amount of data present on the storage.

# Requirements
 Currently the app requires a Syncfusion license if compiled from source as local nuget packages are included.
 Sofar i only tested the app (in its current prototype state) on Windows. It should, but not tested, worked without a problem on linux as well.
 The linux system would need additional setup for Blazor, Microsoft has a Guide on that https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/server?view=aspnetcore-8.0

# Startup
 On Windows:
 - Create a bat file with following command, alter the port how you like: start SearchCacher.exe --urls http://*:80
 - Open Windows firewall (only if the app should be accessed via network)
   - Add the SearchCacher.exe to the allowed apps in the network
 - The app will close automatically after the first open, which creates the config.cfg in the root folder
   - Open the config.cfg file and change at least the SearchPath to the desired folder/drive.
     - Allowed values: \\\\network path\\some folder or D:\\some folder. Json requires all \ to be escaped with another \ -> \\\\
 - Run the bat file again
 - Now open a browser and enter either http://127.0.0.1 or your remote ip for the search
 - Go to Settings and click on Init. Init will run for quite a while depending on how large the storage is

The init can for example take ~40min for 60TB of Data on an SATA SSD RAID with ~30 mio files and 9 mio folders.

ConnectionString is currently a legacy property, it was used for the Redis connection. Redis, however, was not a good choice with some limitations and performance > 10x slower.
Redis may come back depending on the need, but it was like previously mentioned more than 10x slower and also used more then 5x the RAM.

# Performance considerations
Write and read speed is not a requirement on the hosting system, as the file DB will only be written if it is dirty and then after 5 minutes to collect changes and only than write. The file DB is dirty once a path/file is added, removed or updated.

The file DB can be multi GB in size, as such it is recommended to have a fast writing drive so that the search is blocked for a shorter period. But this also depends on the amount of data. For example even a moderate SATA SSD may only take 3-4 seconds for a 1.5 GB file if the cache is not strained.

As for the search itself a fast single core/ few multi core CPU with a good RAM connection and high L2 cache would be best for the fastest results.
E.g. a EPYC 9184X with octa channel would run circles around an i7 with dual channel
