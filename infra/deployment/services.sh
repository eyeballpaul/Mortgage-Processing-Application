#!/usr/bin/env bash

service_project() {
  case "$1" in
    products-api) echo PD.Products.Api ;;
    eligibility-api) echo PD.Eligibility.Api ;;
    mortgage-api) echo PD.Mortgages.Api ;;
    mortgages-web) echo PD.Mortgages.Customer.Web ;;
    staff-web) echo PD.Mortgages.Staff.Web ;;
    notifications-worker) echo PD.Notifications.Worker ;;
    database-bootstrap) echo PD.Mortgages.DatabaseBootstrap ;;
    *) echo "Unknown release target: $1" >&2; return 1 ;;
  esac
}

service_image() {
  case "$1" in
    mortgage-api) echo mortgages-api ;;
    staff-web) echo mortgages-staff-web ;;
    products-api|eligibility-api|mortgages-web|notifications-worker|database-bootstrap) echo "$1" ;;
    *) echo "Unknown release target: $1" >&2; return 1 ;;
  esac
}
