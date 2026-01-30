#!/usr/bin/env bash

STAT="$1"


# This is currently quite empty, but also very simple.
# STAT can be:
#
# "Distro.Name" for the Distro's name [string]
# "Distro.Colors" for the Distro's brand colors from /etc/os-release (in Hex) [string[]]
#

[ "$STAT" = "" ] && exit 01

getDistroName() {

	# This script can definitely be improved and optimized, especially with the large and repetitive if-tree.
	# But it'll make do for now.

    if hostnamectl --version > /dev/null; then
        hostnamectl --json=short | jq -r '.["OperatingSystemPrettyName"]'
        return 0
    fi

    if lsb_release -v > /dev/null; then
        lsb_release -i | grep -oP "Distributor ID:\s+\K.*"
		return 0
    fi


	# To explain these regexes, it tries to match "PRETTY_NAME", and if it can't, it uses "NAME"
	if grep -oP 'PRETTY_NAME="\K[^"]*' < /etc/os-release > /dev/null; then
		grep -oP 'PRETTY_NAME="\K[^"]*' < /etc/os-release
		return 0
	fi
	if grep -oP '(?<!PRETTY_)NAME="\K[^"]*' < /etc/os-release > /dev/null; then
	    grep -oP '(?<!PRETTY_)NAME="\K[^"]*' < /etc/os-release
        return 0
	fi

    echo "Linux OS"
    return 0
}

getDistrocolors() {
	source "$(dirname "$0")"/helpers/colorMap.sh

	ansiColors="$(grep -oP '^ANSI_COLOR="\K[^"]*' /etc/os-release)"

	IFS=";"
	case "$ansiColors" in
		38\;5\;*)
			# 8-bit color
            read -r _ _ colorCode <<< "$ansiColors"
            echo "${BYTECOLORMAP[$colorCode]}"
        ;;

    	0\;38\;5\;*)
    		# 8-bit color, more closely following the standard
            read -r _ _ _ colorCode <<< "$ansiColors"
            echo "${BYTECOLORMAP[$colorCode]}"
        ;;

        38\;2\;*)
        	# TrueColor
        	read -r _ _ redColor greenColor blueColor <<< "$ansiColors"
        	echo -n "#"
        	printf '%X' "$redColor" "$greenColor" "$blueColor"
        ;;

    	0\;38\;2\;*)
    		# TrueColor, more closely following the standard
    		read -r _ _ _ redColor greenColor blueColor <<< "$ansiColors"
            echo -n "#"
            printf '%X' "$redColor" "$greenColor" "$blueColor"
    	;;

        [0-9]\;*)
        	# If this is a 4 bit color, use the appropriate map
        	read -r _ colorCode <<< "$ansiColors"
            echo "${FOURBITCOLORMAP[$colorCode]}"
        ;;

    	*)
    		echo "null"
    	;;
	esac
	unset IFS
}


case $STAT in
	"Distro.Name")
		getDistroName
	;;

	"Distro.Colors")
		getDistrocolors
	;;
esac
