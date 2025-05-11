#!/bin/sh

# Returns in Bytes

STAT="$1"

[ "$STAT" = "" ] && exit 1

REGEX=""

case $STAT in
  	"Total")
    	REGEX="Mem:\s+\K[0-9]+"
    ;;

	"Used")
		REGEX="Mem:\s+[0-9]+\s+\K[0-9]+\s+\K[0-9]+"
	;;

	"Free")
    	REGEX="Mem:\s+[0-9]+\s+\K[0-9]+\s+[0-9]+\s+[0-9]+\s+[0-9]+\s+\K[0-9]+"
    ;;

	"All")
		TOTALREGEX="Mem:\s+\K[0-9]+"
        USEDREGEX="Mem:\s+[0-9]+\s+\K[0-9]+\s+\K[0-9]+"
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
