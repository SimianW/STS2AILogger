# STS2AILogger
A Slay the Spire 2 mod that log user actions and current status to generate data for further ai training.

## Launch data type

The logger tags every JSONL event with a top-level `data_type` field. By default it is `manual`.

Pass one of these launch arguments to mark automated data collection:

```sh
--sts2ailogger-data-type=auto
--sts2ailogger-data-type auto
```

`--sts2ailogger-run-mode`, `--data-type`, and `--run-mode` are also accepted as aliases. The same value can be provided through the `STS2AILOGGER_DATA_TYPE` environment variable.
