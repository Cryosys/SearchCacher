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
  - Open the config.cfg file and change at least the SearchPath to the desired folder/drive
   - Allowed values: \\\\network path\\some folder or D:\\some folder. Json requires all \ to be escaped with another \ -> \\
 - Run the bat file again
 - Now open a browser and enter either http://127.0.0.1 or your remote ip for the search
 - Go to Settings and click on Init. Init will run for quite a while depending on how large the storage is
