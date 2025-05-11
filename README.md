# General Overview
The Bayt API aims to be a unified API to provide useful statistics and controls relating to your homeserver. This provides a stable, yet powerful base for your preferred frontend, and the freedom to choose or make your own frontend without the need to reinvent the wheel with a new backend.

The word "Bayt" comes from Arabic and means "House", because a good house provides you with a powerful, reliable foundation to decorate and make your own however you'd like!

Bayt is still in a very early alpha phase.

More detailed info on Bayt can be found in [the repository's wiki!](https://github.com/Trip7274/Bayt_API/wiki)

## Planned Features:
- [X] Provide general system information
- [X] GPU-related information (NVIDIA, AMD, Intel)
- [X] Proper configuration files (JSON-based)
- [X] Configuration-altering endpoints
- [X] More granular data-fetching endpoints
- [ ] Basic "reference" frontend implementation
- [ ] Binary distribution across different Linux repos
- [ ] Docker container implementation(?)
- [ ] Auth & Basic ratelimiting (authorizing both the frontend "client" and user)
- [ ] SMB Share management endpoints
- [ ] Docker container management endpoints
- [ ] Detailed permissions
- [ ] API documentation

## Dependencies
Currently, this is only supported on Linux using the ASP.NET Core Runtime 9,
and these are the specific dependencies for each GPU vendor:
- NVIDIA Systems:
	- `nvidia-smi`
- Intel GPU Systems:
	- `intel-gpu-tools` (must run `setcap cap_perfmon=+ep /usr/bin/intel_gpu_top` as sudo beforehand)
- AMD GPU Systems:
	- `amdgpu_top`

## Installation and usage
Bayt is still in a very early stage, thus the compiled binaries are unavailable. You're free to compile the source code and test it out, though!
