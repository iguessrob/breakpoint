// Isolated installer failure fixture. Never access SCM, policy, firewall, or shutdown.
if(System.Array.IndexOf(args,"--preflight")>=0 && System.Environment.GetEnvironmentVariable("BREAKPOINT_INSTALLER_FIXTURE_PREFLIGHT")=="1"){System.Console.Error.WriteLine("Fixture: read-only preflight failed.");return 55;}
if(System.Array.IndexOf(args,"--install")>=0){System.Console.Error.WriteLine("Fixture: service setup failed at the test step.");return 51;}
return 0;
