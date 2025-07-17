#!/bin/sh

STAT="$1"

# The expected output format varies depending on $STAT. All units are in Bytes and are expected to be ulong.
#
# $STAT can be:
# "Total" to fetch the total system memory
# "Used" to fetch the used system memory
# "Available" or "Free" to fetch the AVAILABLE system memory
#
# "All" for a list of all the previously stated stats. This is expected to be a string in the format: "Total|Used|Available"

[ "$STAT" = "" ] && exit 1

REGEX=""

case $STAT in
  	"Total")
    	REGEX="Mem:\s+\K[0-9]+"
    ;;

	"Used")
		REGEX="Mem:\s+[0-9]+\s+\K[0-9]+\s+\K[0-9]+"
	;;

	"Free" | "Available")
		# This outputs the AVAILABLE memory, the "Free" case is there for compatibility.
    	REGEX="Mem:\s+[0-9]+\s+\K[0-9]+\s+[0-9]+\s+[0-9]+\s+[0-9]+\s+\K[0-9]+"
    ;;

	"All")
		TOTALREGEX="Mem:\s+\K[0-9]+"
        USEDREGEX="Mem:\s+[0-9]+\s+\K[0-9]+"
        AVAILABLEREGEX="Mem:\s+[0-9]+\s+\K[0-9]+\s+[0-9]+\s+[0-9]+\s+[0-9]+\s+\K[0-9]+"

		OUTPUT="$(free -b)"
		TOTAL="$(echo "$OUTPUT" | grep -oP "$TOTALREGEX")"
		USED="$(echo "$OUTPUT" | grep -oP "$USEDREGEX")"
		AVAILABLE="$(echo "$OUTPUT" | grep -oP "$AVAILABLEREGEX")"

		echo "$TOTAL|$USED|$AVAILABLE"
		exit 0
	;;

	*)
    	exit 2
	;;
esac


free -b | grep -oP "$REGEX"
