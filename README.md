# CounterPartMusic
The app processes and updates periodic snapshots of music data (e.g., recordings, unclaimed works, work right shares etc.) held in raw TSV format from the Mechanical Licensing Collective's (MLC) SFTP server into a Snowflake database. Each snapshot includes around 290+ GB of data across 12 tables, with up to 930 million records in a single table. Leveraged multithreading, large tsv file splitting, parallel uploads, and the Snowflake C# connector to ensure efficient synchronization and analytics.

# Workflow
The app periodically checks for MLC data snapshot updates in their sftp server based on a configured interval stored in the `appconfig` table in Snowflake. It downloads large raw TSV files, splits them into chunks, and uses multithreading with Snowflake's .NET data connector to load the chunks into Snowflake tables. The process also includes logging and exception handling.

# Setup
    1. Store the MLC SFTP server's username and private key path in the "Username" and "PrivateKeyPath" fields of the appsettings.
    2. Fill in the placeholders in the "ConnectionString" -> "DefaultConnection" section of appsettings with your Snowflake account credentials:  
        `Account=<accountidentifier>.<region>.<cloud hostname>;User=<username>;Password=<password>;`
    3. Open a worksheet in the Snowflake web interface and execute the scripts in `scripts/Snowflake.txt` to set up Snowflake objects like tables and file formats.
    4. To host on a Linux system like Ubuntu 22.04, install the .NET Runtime with the following commands:  

    ```bash
    wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb  
    sudo dpkg -i packages-microsoft-prod.deb  
    sudo apt-get update  
    sudo apt-get install -y dotnet-runtime-8.0  
    dotnet --list-runtimes  
    which dotnet  
    ```  
    5. To run your app as a background service on Ubuntu, create and configure a systemd service file:
    ```bash sudo nano /etc/systemd/system/counterpartmusic.service```
    Add the following configuration, updating WorkingDirectory and ExecStart with your app's folder and .NET runtime path:
    ``` bash [Unit]
        Description=Counterpartmusic App
        After=network.target

        [Service]
        WorkingDirectory=/usr/counterpartmusic/app
        ExecStart=/usr/bin/dotnet /usr/counterpartmusic/app/CounterPartMusic.dll
        Restart=always
        RestartSec=10
        KillSignal=SIGINT
        SyslogIdentifier=mydotnetapp
        User=root
        Environment=ASPNETCORE_ENVIRONMENT=Production
        Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

        [Install]
        WantedBy=multi-user.target
    ```
    Save the file and reload systemd:
    ```bash sudo systemctl daemon-reload```
    Enable and start the service:
    ```bash sudo systemctl enable counterpartmusic.service
       sudo systemctl start counterpartmusic.service```
    Check the service status:
    ```bash sudo systemctl status counterpartmusic.service```



