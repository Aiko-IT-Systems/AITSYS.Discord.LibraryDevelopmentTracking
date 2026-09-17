#!/bin/bash
git pull
service discord-lib-dev-tracking stop
linux/publish.sh
service discord-lib-dev-tracking start
