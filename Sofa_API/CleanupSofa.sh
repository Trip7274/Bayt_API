#!/usr/bin/env bash

logHelper() {
	# Expects $1 to be the message itself.
	# Expects $2 to be either "INFO", "OK", "WARNING", "ERROR", or "FATAL". Defaults to "INFO" in case this is empty
	case "$2" in
	"OK")
		PREFIX="\e[0;32m[OK]"
	;;

	"WARNING")
		PREFIX="\e[0;33m[WARNING]"
	;;

	"ERROR")
		PREFIX="\e[0;31m[ERROR]"
	;;

	"FATAL")
		PREFIX="\e[1;31m[FATAL]"
	;;

	*)
    	PREFIX="[INFO]"
    ;;
	esac
	printf "$PREFIX %s\e[0m\n" "$1"
}

printf "We're sorry to see you go! If you're having any issues, do feel free to report them in our GitHub repo! (https://github.com/Trip7274/Sofa_API)\nThis script will reverse everything that \e[3;36mSetupSofa.sh\e[0m set up, and delete any Sofa-related files.\n\n"
if [ "$SETUP_NOCONFIRM" != "1" ]; then
    printf "Most of this process is \e[0;33muser-specific\e[0m. This means that '\e[0;32m%s\e[0m' may not be the only user with permissions setup.\nTo check for all users, make sure the directory '/etc/sudoers.d/' is cleared of any files \e[4;37mstarting with the characters '10-SofaApi-[user]'\e[0m.\nPress enter to continue, or Ctrl+C to cancel\n" "$USER"
	read -r _
	printf "\n"
fi

logHelper "Checking if the user has any special permissions set"
if sudo test -f "/etc/sudoers.d/10-SofaApi-$USER"; then
	sudo rm "/etc/sudoers.d/10-SofaApi-$USER"

    logHelper "Permissions were removed successfully. Thank you for trying Sofa!" "OK"

else
	logHelper "Seems like the user doesn't have any special permissions." "INFO"
fi

printf "\n"

if [ "$SETUP_DELETE_DATA" == "0" ]; then
	exit 0
elif [ "$SETUP_NON_INTERACTIVE" == "1" ] && [ "$SETUP_DELETE_DATA" != "1" ]; then
	logHelper "SETUP_DELETE_DATA was not set to '1' while in interactive mode, deciding to skip data deletion."
	exit 0
fi

sofaDataFolder="sofaData"

if [ -n "$SOFA_DATA_DIRECTORY" ]; then
	sofaDataFolder="$SOFA_DATA_DIRECTORY/$sofaDataFolder"
elif [ -n "$XDG_DATA_HOME" ]; then
	sofaDataFolder="$XDG_DATA_HOME/$sofaDataFolder"
elif [ -d "$(dirname "$0")/$sofaDataFolder" ]; then
	sofaDataFolder="$(dirname "$0")/$sofaDataFolder"
else
	unset sofaDataFolder
fi


sofaConfigFolder="sofaConfig"

if [ -n "$SOFA_CONFIG_DIRECTORY" ]; then
    sofaConfigFolder="$SOFA_CONFIG_DIRECTORY/$sofaConfigFolder"
elif [ -n "$XDG_CONFIG_HOME" ]; then
	sofaConfigFolder="$XDG_CONFIG_HOME/$sofaConfigFolder"
elif [ -d "$(dirname "$0")/$sofaConfigFolder" ]; then
	sofaConfigFolder="$(dirname "$0")/$sofaConfigFolder"
else
	unset sofaConfigFolder
fi

if [ -z "$sofaConfigFolder" ] && [ -z "$sofaDataFolder" ]; then
	logHelper "Unable to find any Sofa-related files. Sofa might not have been started yet." "WARNING"
	logHelper "Another cause of this could be if you set custom 'SOFA_CONFIG/DATA_DIRECTORY' environment variables for Sofa, but not this script. If so, try running this again with those environment variables set."
	exit 1
fi

if [ "$SETUP_NON_INTERACTIVE" != "1" ] && [ "$SETUP_DELETE_DATA" != "1" ]; then
	printf "Would you like to delete Sofa's data? (config files, logs, \e[1;31mDocker container compose files\e[0m, etc.)\n"
	printf "The identified locations are:\nConfigs: \e[34m%s\e[0m\nData: \e[33m%s\e[0m\n\n" "${sofaConfigFolder:-"(Unknown, might be non-existent or custom?)"}" "${sofaDataFolder:-"(Unknown, might be non-existent or custom?)"}"
	printf "Press Enter to continue, or Ctrl+C to cancel.\n"
    read -r _
    printf "\n"
fi

if [ -n "$sofaDataFolder" ] && [ -d "$sofaDataFolder" ]; then
	logHelper "Deleting $sofaDataFolder..."
    rm -rf "$sofaDataFolder"
    logHelper "Done!" "OK"
fi

if [ -n "$sofaConfigFolder" ] && [ -d "$sofaConfigFolder" ]; then
	logHelper "Deleting $sofaConfigFolder..."
    rm -rf "$sofaConfigFolder"
    logHelper "Done!" "OK"
fi

logHelper "Sofa has been removed from your system and user privileges were revoked. Thank you for trying Sofa! <3" "OK"