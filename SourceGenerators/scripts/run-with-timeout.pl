#!/usr/bin/env perl

use strict;
use warnings;
use POSIX qw(WNOHANG setsid);
use Time::HiRes qw(sleep time);

my $seconds = shift @ARGV;
die "Usage: run-with-timeout.pl <positive-seconds> <command> [args...]\n"
  unless defined $seconds && $seconds =~ /\A[1-9][0-9]*\z/ && @ARGV;

my $pid = fork();
die "Unable to fork timeout child: $!\n" unless defined $pid;
if ($pid == 0) {
  die "Unable to create timeout process group: $!\n" if setsid() == -1;
  exec @ARGV;
  die "Unable to execute $ARGV[0]: $!\n";
}

my $interrupted_exit = 0;
$SIG{TERM} = sub { $interrupted_exit = 143; };
$SIG{INT} = sub { $interrupted_exit = 130; };
my $deadline = time() + $seconds;
my $status;

while (1) {
  my $waited = waitpid($pid, WNOHANG);
  if ($waited == $pid) {
    $status = $?;
    last;
  }
  if ($waited == -1) {
    die "Unable to wait for timeout child: $!\n";
  }
  if ($interrupted_exit || time() >= $deadline) {
    kill 'KILL', -$pid;
    waitpid($pid, 0);
    exit($interrupted_exit || 124);
  }
  sleep 0.05;
}

# A command in this controller must not leave helpers running after its leader
# exits. Docker and cleanup commands are synchronous, so remaining group members
# are always abandoned work.
kill 'KILL', -$pid;
if (($status & 127) != 0) {
  exit(128 + ($status & 127));
}
exit($status >> 8);
