#!/bin/sh

# STAT can be:
# "Distro.Name" for the Distro's name
# TODO: Would be great if we could get the ANSI colors (from /etc/os-release) and convert them to HEX, to help frontend customization.
#

STAT="$1"

[ "$STAT" = "" ] && exit 01

getDistroName() {

    if hostnamectl --json=short | jq -r '.["OperatingSystemPrettyName"]' > /dev/null; then
        hostnamectl --json=short | jq -r '.["OperatingSystemPrettyName"]'
        return 0
    fi

    if lsb_release -i | grep -oP "Distributor ID:\s+\K.*" > /dev/null; then
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


case $STAT in
	"Distro.Name")
		getDistroName
	;;
esac