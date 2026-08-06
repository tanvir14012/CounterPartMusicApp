# CounterPartMusic
The app processes and updates periodic snapshots of music data (e.g., recordings, unclaimed works, work right shares etc.) held in raw TSV format from the Mechanical Licensing Collective's (MLC) SFTP server into a Snowflake database. Each snapshot includes around 290+ GB of data across 12 tables, with up to 930 million records in a single table. Leveraged multithreading, large tsv file splitting, parallel uploads, and the Snowflake C# connector to ensure efficient synchronization and analytics.

# Workflow
The app periodically checks for MLC data snapshot updates in their sftp server based on a configured interval stored in the `appconfig` table in Snowflake. It downloads large raw TSV files, splits them into chunks, and uses multithreading with Snowflake's .NET data connector to load the chunks into Snowflake tables. The process also includes logging and exception handling.

# C# Setup
1. Store the MLC SFTP server's username and private key path in the "Username" and "PrivateKeyPath" fields of the appsettings.
2. Fill in the placeholders in the "ConnectionString" -> "DefaultConnection" section of appsettings with your Snowflake account credentials:  
   `Account=<accountidentifier>.<region>.<cloud hostname>;User=<username>;******;`
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
```bash
sudo nano /etc/systemd/system/counterpartmusic.service
```
Add the following configuration, updating WorkingDirectory and ExecStart with your app's folder and .NET runtime path:

```bash
[Unit]
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
```bash
sudo systemctl daemon-reload
```
Enable and start the service:
```bash
sudo systemctl enable counterpartmusic.service
sudo systemctl start counterpartmusic.service
```
Check the service status:
```bash
sudo systemctl status counterpartmusic.service
```

# TypeScript/Node.js Implementation
The Node.js side-by-side implementation is available under `Node/` and reproduces the C# snapshot workflow:
1. Reads runtime app settings from `MASTER.APPSETTINGS`.
2. Connects to MLC SFTP using private key authentication.
3. Detects latest snapshot and supports targeted reload by `SCHEMA.TABLE` from `TablesToReloadImmediately`.
4. Downloads required TSV files only.
5. Splits large TSV files into chunk files at line boundaries.
6. Uploads chunks to Snowflake stage and loads with `COPY INTO`.
7. Writes logs to both console and file.
8. Inserts run metrics into `MASTER.APPLOG`.
9. Loops according to `SnapshotCheckIntervalInHours` and `AutoSnapshotUpdate`.

## Node Project Structure
```text
Node/
  .env.example
  package.json
  src/
    index.js
    config/env.js
    constants/tableMap.js
    services/
      configRepository.js
      sftpService.js
      snowflakeClient.js
      snapshotLoader.js
    utils/
      logger.js
      sql.js
      tsvSplitter.js
```

## Node Setup
1. Copy `Node/.env.example` to `Node/.env` and fill all required values.
2. Install dependencies:
```bash
cd Node
npm install
```
3. Start:
```bash
npm start
```

## Linux Deployment (Nginx + Node + PM2)
1. Install Node.js LTS, Nginx, and PM2:
```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt-get update
sudo apt-get install -y nodejs nginx
sudo npm install -g pm2
node -v
npm -v
pm2 -v
```
2. Deploy app and install packages:
```bash
sudo mkdir -p /usr/counterpartmusic
sudo chown -R $USER:$USER /usr/counterpartmusic
cd /usr/counterpartmusic
git clone <your-repo-url> app
cd app/Node
cp .env.example .env
npm install --omit=dev
```
3. Create PM2 ecosystem file (`/usr/counterpartmusic/app/Node/ecosystem.config.cjs`):
```javascript
module.exports = {
  apps: [
    {
      name: "counterpartmusic-node",
      cwd: "/usr/counterpartmusic/app/Node",
      script: "src/index.js",
      instances: 1,
      exec_mode: "fork",
      autorestart: true,
      watch: false,
      max_memory_restart: "2G",
      env: {
        NODE_ENV: "production"
      }
    }
  ]
};
```
4. Start and persist PM2:
```bash
cd /usr/counterpartmusic/app/Node
pm2 start ecosystem.config.cjs
pm2 save
pm2 startup systemd -u $USER --hp /home/$USER
```
5. Optional systemd fallback for PM2 process manager:
```bash
sudo systemctl enable pm2-$USER
sudo systemctl start pm2-$USER
```
6. Configure Nginx reverse proxy (`/etc/nginx/sites-available/counterpartmusic-node`):
```nginx
server {
    listen 80;
    server_name your-domain-or-host;

    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```
Enable and reload Nginx:
```bash
sudo ln -s /etc/nginx/sites-available/counterpartmusic-node /etc/nginx/sites-enabled/counterpartmusic-node
sudo nginx -t
sudo systemctl restart nginx
```
7. Operations:
```bash
pm2 status
pm2 logs counterpartmusic-node
pm2 restart counterpartmusic-node
pm2 stop counterpartmusic-node
sudo systemctl status nginx
```
