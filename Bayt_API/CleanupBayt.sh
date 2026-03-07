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

printf "We're sorry to see you go! If you're having any issues, do feel free to report them in our GitHub repo! (https://github.com/Trip7274/Bayt_API)\nThis script will reverse everything that \e[3;36mSetupBayt.sh\e[0m set up, and delete any Bayt-related files.\n\n"
if [ "$SETUP_NOCONFIRM" != "1" ]; then
    printf "Most of this process is \e[0;33muser-specific\e[0m. This means that '\e[0;32m%s\e[0m' may not be the only user with permissions setup.\nTo check for all users, make sure the directory '/etc/sudoers.d/' is cleared of any files \e[4;37mstarting with the characters '10-BaytApi-[user]'\e[0m.\nPress enter to continue, or Ctrl+C to cancel\n" "$USER"
	read -r _
	printf "\n"
fi

logHelper "Checking if the user has any special permissions set"
if sudo test -f "/etc/sudoers.d/10-BaytApi-$USER"; then
	sudo rm "/etc/sudoers.d/10-BaytApi-$USER"

    logHelper "Permissions were removed successfully. Thank you for trying Bayt!" "OK"

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

baytDataFolder="baytData"

if [ -n "$BAYT_DATA_DIRECTORY" ]; then
	baytDataFolder="$BAYT_DATA_DIRECTORY/$baytDataFolder"
elif [ -n "$XDG_DATA_HOME" ]; then
	baytDataFolder="$XDG_DATA_HOME/$baytDataFolder"
elif [ -d "$(dirname "$0")/$baytDataFolder" ]; then
	baytDataFolder="$(dirname "$0")/$baytDataFolder"
else
	unset baytDataFolder
fi


baytConfigFolder="baytConfig"

if [ -n "$BAYT_CONFIG_DIRECTORY" ]; then
    baytConfigFolder="$BAYT_CONFIG_DIRECTORY/$baytConfigFolder"
elif [ -n "$XDG_CONFIG_HOME" ]; then
	baytConfigFolder="$XDG_CONFIG_HOME/$baytConfigFolder"
elif [ -d "$(dirname "$0")/$baytConfigFolder" ]; then
	baytConfigFolder="$(dirname "$0")/$baytConfigFolder"
else
	unset baytConfigFolder
fi

if [ -z "$baytConfigFolder" ] && [ -z "$baytDataFolder" ]; then
	logHelper "Unable to find any Bayt-related files. Bayt might not have been started yet." "WARNING"
	logHelper "Another cause of this could be if you set custom 'BAYT_CONFIG/DATA_DIRECTORY' environment variables for Bayt, but not this script. If so, try running this again with those environment variables set."
	exit 1
fi

if [ "$SETUP_NON_INTERACTIVE" != "1" ] && [ "$SETUP_DELETE_DATA" != "1" ]; then
	printf "Would you like to delete Bayt's data? (config files, logs, \e[1;31mDocker container compose files\e[0m, etc.)\n"
	printf "The identified locations are:\nConfigs: \e[34m%s\e[0m\nData: \e[33m%s\e[0m\n\n" "${baytConfigFolder:-"(Unknown, might be non-existent or custom?)"}" "${baytDataFolder:-"(Unknown, might be non-existent or custom?)"}"
	printf "Press Enter to continue, or Ctrl+C to cancel.\n"
    read -r _
    printf "\n"
fi

if [ -n "$baytDataFolder" ] && [ -d "$baytDataFolder" ]; then
	logHelper "Deleting $baytDataFolder..."
    rm -rf "$baytDataFolder"
    logHelper "Done!" "OK"
fi

if [ -n "$baytConfigFolder" ] && [ -d "$baytConfigFolder" ]; then
	logHelper "Deleting $baytConfigFolder..."
    rm -rf "$baytConfigFolder"
    logHelper "Done!" "OK"
fi

logHelper "Bayt has been removed from your system and user privileges were revoked. Thank you for trying Bayt! <3" "OK"