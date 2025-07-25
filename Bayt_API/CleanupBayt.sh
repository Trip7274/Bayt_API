#!/bin/sh

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
	printf "$PREFIX %s\033[0m\n" "$1"
}

printf "We're sorry to see you go! If you're having any issues, do feel free to report them in our GitHub repo!\nThis script will reverse everything that SetupBayt.sh set up."
if [ "$SETUP_NOCONFIRM" != "1" ]; then
    printf "This process is \e[0;33muser-specific\033[0m. This means that '%s' may not be the only user with permissions setup.\nTo check for all users, make sure the directory '/etc/sudoers.d/' is cleared of any files \e[4;37mstarting with the characters '10-BaytApi-[user]'\033[0m.\nPress enter to continue, or Ctrl+C to cancel\n" "$USER"
	read -r _
fi

logHelper "Checking if the user has any special permissions set"
if sudo test -f "/etc/sudoers.d/10-BaytApi-$USER"; then
	sudo rm "/etc/sudoers.d/10-BaytApi-$USER"

    logHelper "Permissions were removed successfully. Thank you for trying Bayt!" "OK"

else
	logHelper "Seems like the user doesn't have any special permissions." "INFO"
fi