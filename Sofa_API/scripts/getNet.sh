#!/bin/sh

STAT="$1"
IPADDR="$2"

# The expected format is a bare string.
# $IPADDR is expected only when $STAT is "PhysicalAddress".
#
# $STAT can be:
# "PhysicalAddress" to retrieve the Physical (or MAC) address of a specific machine in the LAN. Falls back to "null" in case it was not found. (TODO: Add more fallback options to fetch the physical address)
# "NetworkDevice" for the current device's default network device. Example: "enp14s0"
# "Netmask" for the default network devices's netmask. Example: "255.255.255.0"
# "LocalAddress" for the current device's local IP address. Example: "192.168.1.2"
#

[ "$STAT" = "" ] && echo null && exit 01

NETDEV="$(ip -o -f inet route | grep -oP "(wl[^\s]*)?(en[^\s]*)?" | head -n 1)"
LOCALADDR="$(ifconfig "$NETDEV" | grep -oP "inet\s+\K[0-9]+.[0-9]+.[0-9]+.[0-9]+")"


case "$STAT" in
	"PhysicalAddress")
		[ "$IPADDR" = "" ] && echo null && exit 02

		if [ "$IPADDR" = "$LOCALADDR" ] || [ "$IPADDR" = "127.0.0.1" ] || [ "$IPADDR" = "localhost" ]; then
		    PHYSICALADDR="$(ifconfig "$NETDEV" | grep -oP "ether\s+\K[0-9|a-f]{2}:[0-9|a-f]{2}:[0-9|a-f]{2}:[0-9|a-f]{2}:[0-9|a-f]{2}:[0-9|a-f]{2}")"

		else
		    PHYSICALADDR="$(grep -oP "$IPADDR\s+[0-9]x[0-9]\s+[0-9]x[0-9]\s+\K[^\s]*" < /proc/net/arp)"
		fi

		if [ "$PHYSICALADDR" = "" ]; then
		    echo null

		else
		    echo "$PHYSICALADDR"
		fi
	;;

	"NetworkDevice")
		echo "$NETDEV"
	;;

	"Netmask")
		ifconfig "$NETDEV" | grep -oP "netmask\s+\K[0-9]+.[0-9]+.[0-9]+.[0-9]+"
	;;

	"LocalAddress")
		echo "$LOCALADDR"
	;;
esac
