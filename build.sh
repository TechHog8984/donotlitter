#!/usr/bin/env bash

gcc -shared -fPIC -o libdonotlitter.so donotlitter.c -ldl -lpthread
