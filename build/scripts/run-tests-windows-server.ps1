$BOOTSTRAP_APP_PATH=$args[0]
$BOOTSTRAP_APP_EXE=$args[1]
$BOOTSTRAP_TEST_RUNNER_PATH=$args[2]
$env:BOOTSTRAP_TEST_RUNNER_URL=$args[3]

cd $BOOTSTRAP_APP_PATH
$serverProcess = Start-Process dotnet -NoNewWindow -PassThru

Try 
{
	cd $BOOTSTRAP_TEST_RUNNER_PATH
	npm install
	node app
}
Finally
{
	$serverProcess.Kill()
}