reactor-announcer = Reactor Control
reactor-console-id-card = operator ID card

reactor-announcement-online = Fusion reactor online. Automatic control engaged.
reactor-announcement-warning = Reactor containment integrity dropping. Engineering, respond.
reactor-announcement-critical = Reactor containment critical. Immediate action required.
reactor-announcement-disruption = REACTOR DISRUPTION. Containment lost.
reactor-announcement-scram = Reactor SCRAM engaged. Emergency quench in progress.

reactor-alarm-scrammed = SCRAM ENGAGED — reactor quenched, restart locked out.
reactor-alarm-warning = WARNING — containment integrity dropping.
reactor-alarm-critical = CRITICAL — containment failure imminent.

reactor-popup-not-running = The reactor isn't online.
reactor-popup-not-manual = Switch to manual mode to adjust this directly.
reactor-popup-wrong-startup-step = Startup sequence expects {$command} next.
reactor-popup-no-printer-linked = No printer is linked to this console.
reactor-popup-printed = The printer chatters out a fresh page.

reactor-fault-beta = BETA LIMIT EXCEEDED
reactor-fault-density = DENSITY LIMIT EXCEEDED
reactor-fault-coil = COIL {$index} OVERHEATING
reactor-auto-derated = AUTO OUTPUT CAPPED — LOW INTEGRITY

reactor-output = OUTPUT {$value}%
reactor-demand = DEMAND {$value}%
reactor-beta = BETA {$value}%
reactor-density = DENSITY {$value}%
reactor-ash = ASH {$value}%
reactor-integrity = INTEGRITY {$value}%
reactor-coil-readout = COIL {$index}  {$current}/{$target}%

reactor-sequence-step = {$step} — {$progress}%
reactor-sequence-awaiting = {$step} — AWAITING {$command} COMMAND

reactor-terminal-welcome = Terminal ready. Type HELP for a list of commands.

reactor-tab-controls = Controls
reactor-tab-charts = Status Charts

reactor-login-prompt = INSERT CARD TO AUTHENTICATE
reactor-login-granted = ACCESS GRANTED — WELCOME, {$name}
reactor-login-denied = ACCESS DENIED — INSUFFICIENT CLEARANCE
reactor-login-revoked = ACCESS REVOKED — CARD REMOVED
reactor-operator-label = Operator: {$name}
reactor-popup-not-logged-in = The console rejects the command — insert an ID card to log in first.
reactor-popup-no-reactor-selected = No reactor is currently selected. Use SELECT or REACTORS.

reactor-chart-output = OUTPUT / DEMAND
reactor-chart-fueling = FUELING
reactor-chart-heating = HEATING
reactor-chart-current = PLASMA CURRENT
reactor-chart-divertor = DIVERTOR
reactor-chart-coils = COILS (avg)
reactor-chart-beta = BETA (vs limit)
reactor-chart-density = DENSITY (vs limit)

signal-port-name-reactor-printer-sender = Reactor printer
signal-port-description-reactor-printer-sender = Reactor printer signal sender

signal-port-name-reactor-printer-receiver = Reactor printer
signal-port-description-reactor-printer-receiver = Reactor printer signal receiver
