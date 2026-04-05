#!/bin/sh

STAT="$1"

# The expected output format varies depending on $STAT. All units are in Bytes and are expected to be ulong.
#
# $STAT can be:
# "Total" to fetch the total system memory
# "Used" to fetch the used system memory
# "Available" to fetch the AVAILABLE system memory
# "Free" to fetch the FREE system memory (https://www.linuxatemyram.com/)
#
# "All" for a list of all the previously stated stats. This is expected to be a string in the format: "Total|Used|Available"

[ "$STAT" = "" ] && exit 1

REGEX=""

case $STAT in
  	"Total")
    	REGEX="MemTotal:\s+\K[0-9]+"
    ;;

	"Used")
		REGEX="Active:\s+\K[0-9]+"
	;;

	"Free")
    	REGEX="MemFree:\s+\K[0-9]+"
	;;

	"Available")
		# This outputs the AVAILABLE memory, the "Free" case is there for compatibility.
    	REGEX="MemAvailable:\s+\K[0-9]+"
    ;;

	"All")
		TOTALREGEX="MemTotal:\s+\K[0-9]+"
        USEDREGEX="Active:\s+\K[0-9]+"
        AVAILABLEREGEX="MemAvailable:\s+\K[0-9]+"

		OUTPUT="$(cat /proc/meminfo)"
		TOTAL="$(($(echo "$OUTPUT" | grep -oP "$TOTALREGEX") * 1000))"
		USED="$(($(echo "$OUTPUT" | grep -oP "$USEDREGEX") * 1000))"
		AVAILABLE="$(($(echo "$OUTPUT" | grep -oP "$AVAILABLEREGEX") * 1000))"

		echo "$TOTAL|$USED|$AVAILABLE"
		exit 0
	;;

	*)
    	exit 2
	;;
esac


outputKbs="$(grep -oP "$REGEX" /proc/meminfo)"
echo "$(( outputKbs * 1000  ))"
