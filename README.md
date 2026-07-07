# condeco-cli
Access Condeco via a command line interface.

Please leave a star :)

## Download
Portable executables for Windows, Linux and Mac can be found over in the [releases](https://github.com/fiddyschmitt/condeco-cli/releases/latest) section.

## Usage

Run the program to add the desired bookings:

![image](https://github.com/user-attachments/assets/60f0aac7-fc62-4894-93d5-e93cfef4081b)

## Usage
To book all the items in the config file:

`condeco-cli.exe --autobook`

![autobook](https://github.com/user-attachments/assets/e8570996-caae-462c-9b39-21888d5b4326)

To check in:

`condeco-cli.exe --checkin`

## Scheduling
To run a Linux cron job, follow [these](https://github.com/fiddyschmitt/condeco-cli/wiki/Scheduling-in-Linux) instructions.

To run in a Windows Scheduled Task, follow [these](https://github.com/fiddyschmitt/condeco-cli/wiki/Scheduling-in-Windows) instructions.

## Docker

A Docker image is available from [GitHub Container Registry](https://github.com/fiddyschmitt/condeco-cli/pkgs/container/condeco-cli) and is automatically built whenever a new release is published. By default, the docker image runs --autobook but this can be overriden by passing command line arguments.

Pull the latest image:

```bash
docker pull ghcr.io/fiddyschmitt/condeco-cli:latest
```

Run autobook with your config file mounted:

```bash
docker run --rm \
  -v /path/to/config.ini:/app/config.ini \
  ghcr.io/fiddyschmitt/condeco-cli:latest
```

Run checkin with your config file mounted:

```bash
docker run --rm \
  -v /path/to/config.ini:/app/config.ini \
  ghcr.io/fiddyschmitt/condeco-cli:latest --checkin
```

> **Note:** On SELinux-enabled systems (e.g. Fedora, RHEL), append `:Z` to the volume mount flag to avoid permission errors:
> `-v path/to/config.ini:/app/config.ini:Z`

To use with a scheduler, add a cron entry on the host that calls `docker run` instead of the bare binary — see the [Scheduling in Linux](https://github.com/fiddyschmitt/condeco-cli/wiki/Scheduling-in-Linux) wiki page for the general approach.

## Thanks

Thanks to those who bought me a coffee!

<a href="https://www.buymeacoffee.com/fidel248" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" ></a>
