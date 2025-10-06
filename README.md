# General Overview
Bayt aims to be a simple REST API to provide useful statistics and controls relating to your homeserver.
This provides a stable, yet powerful base for your preferred frontend,
and the freedom to choose or make your own frontend without the need to reinvent the wheel with a new backend.

The word "Bayt" comes from Arabic and means "House", because a good house provides you with a good,
reliable foundation to decorate and make your own however you'd like!

Bayt is still in an early alpha phase.

## Planned Features:
- [X] Provide general system information
- [X] GPU-related information (NVIDIA, AMD, Intel)
- [X] Proper configuration files (JSON-based)
- [X] Configuration-altering endpoints
- [X] More granular data-fetching endpoints
- [ ] Basic "reference" frontend implementation (Delayed as I have very little experience with frontends,
please do help out if you have the experience!)
- [ ] More server-management endpoints
	- [X] WoL management and support
	- [X] Shutdown and restart endpoints
	- [X] Arbitrary data exchange endpoints for things like client-wide configs
	- [ ] Docker container management endpoints
    - [ ] Docker image management endpoints
- [ ] Auth & Basic ratelimiting (authorizing both the frontend "client" and user)
- [ ] Detailed permissions
	- [ ] Silo arbitrary data folder to each client
	- [ ] Add a permission to manage clients (with a dangerous disclaimer)
	- [ ] Add a way to revoke clients without relying on any clients (Helper CLI?)
	- [ ] Establish permission presets (Simple stat-viewing dashboards need less access than server management clients)
- [ ] Add Bayt log streaming endpoints
- [ ] API documentation
- [ ] Binary distribution across different Linux repos
- [ ] Docker container implementation (if possible)
- *----1.0 Release!----*
- [ ] Plugin support (if practical)
- *Do feel free to suggest some more features!*

## Architecture
Bayt uses simple scripts (located in `Bayt_API/scripts/`) to fetch system information, thus allowing you to modify its implementations to suit your system without knowing C#.
The scripts can be written in any language that can be invoked using a shebang. More details on the specific format of I/O can be found in the script headers.

## Dependencies
Currently, Bayt is only supported on Linux using the [ASP.NET Core Runtime 9](https://learn.microsoft.com/en-us/dotnet/core/install/linux), and any other dependencies are script-specific,
but the default scripts require the following, depending on your GPU:
- NVIDIA Systems: (looking for testers; progress paused)
	- `nvidia-smi`
- Intel GPU Systems:
	- `intel-gpu-tools` (must run `setcap cap_perfmon=+ep /usr/bin/intel_gpu_top` as sudo beforehand)
- AMD GPU Systems:
	- `amdgpu_top` (Currently requires a build that contains the commit [04983eb](https://github.com/Umio-Yasuno/amdgpu_top/commit/04983ebf5563982c9d685e587a8a1f2a48252811). Either compile manually or use the [amdgpu_top-git](https://aur.archlinux.org/packages/amdgpu_top-git) AUR package)

Along with utilites you probably already have, such as `bash`, (GNU) `grep`, `head`, `jq`, `net-tools`, and `df`.

---
I mainly test this on CachyOS (Arch-based) and a plain Fedora server, but I do try to use as many distro-agnostic features as I can in the default scripts.
If you encounter any issues on other distros, feel free to open an issue!

This project also uses shell scripts to fetch most system stats (located in the `Bayt_API/scripts` directory),
so you shouldn't need C#-specific knowledge to troubleshoot any of the system-facing interactions.
Do be sure to output the data in the appropriate format, though!

You can find the proper format and documentation in each script's head.

## Installation and usage
Bayt is still in an early stage, thus the compiled binaries are unavailable.
You're free to compile the source code and test it out, though!

If you'd like to tinker with it, compilation should be quite simple. But make sure to [grab the .NET SDK 9.0 beforehand](https://learn.microsoft.com/en-us/dotnet/core/install/linux)!

1. Retrieve the latest branch using `git clone https://github.com/Trip7274/Bayt_API.git`
2. Switch to the appropriate directory using `cd Bayt_API/Bayt_API/`
3. Compile by running `dotnet build --configuration Release`
4. Switch to the output directory using `cd bin/Release/net9.0`
5. Execute the server binary using `./Bayt_API`

If you'd like it as a one-liner, here you go:
```shell
git clone https://github.com/Trip7274/Bayt_API.git; cd Bayt_API/Bayt_API/; dotnet build --configuration Release; cd bin/Release/net9.0; ./Bayt_API
```
