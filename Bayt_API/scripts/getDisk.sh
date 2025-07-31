#!/bin/sh

STAT="$1"
TARGETPATH="$2"

# This part of Bayt is one of the first to be handled in a "hybrid" way. This means that, if a script has a specific $STAT
# in its "SCRIPTSUPPORTS" variable (which should be echoed if the $STAT is "Meta.Supports"),
# then Bayt will execute the script for that stat, rather than the internal C# method.
# This is to maintain the customizability of Bayt, while allowing more functions to be implemented natively by default.
# You can edit this variable as you add more code to this script to handle more features.

SCRIPTSUPPORTS="" # By default, this script file is unused. Example for this var: "Device.Path|Device.Filesystem"

# $TARGETPATH can either be an actual path on the filesystem (e.g. "/home"), or a device file path (e.g. "/dev/nvme0n1p3"). Check below for conditions.
# $STAT can be:
#
# "Device.Path" to fetch the device file path from a specific path on the filesystem. Example: "/home" results in "/dev/nvme0n1p3" [string]
# $TARGETPATH is expected to be a path in the file system in this branch. ("/home") ^
#
# vvv $TARGETPATH = device file path (/dev/nvme0n1p3) from this point down vvv
#
# "Device.Filesystem" for the type of filesystem the parititon uses. Example: "/dev/nvme0n1p3" results in "btrfs". [string]
#
# "Partition.TotalSpace" for the total size of the partition. In bytes. [ulong]
#
# "Parition.FreeSpace" for however many bytes are available on this partition. [ulong]
#
# "Meta.Supports" for all supported features (or $STATs) in this script. Does not expect any $TARGETPATH (Array of strings)

[ ! -e "$TARGETPATH" ] && [ "$STAT" != "Meta.Supports" ] && echo null && exit 01
[ "$STAT" = "" ] && echo null && exit 02


case "$STAT" in
	"Meta.Supports")
		echo "$SCRIPTSUPPORTS" # This should always be here.
	;;

	*)
		echo "null"
	;;
esac
