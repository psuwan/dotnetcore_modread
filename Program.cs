using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using NModbus;
using NModbus.Serial;
using MySql.Data.MySqlClient;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Please provide the path to the config.csv file as a command-line argument.");
            return;
        }

        string csvFilePath = args[0]; // Path to the CSV file

        if (!File.Exists(csvFilePath))
        {
            Console.WriteLine("The specified config.csv file does not exist.");
            return;
        }

        string[] csvLines = File.ReadAllLines(csvFilePath);

        ModbusFactory factory = new ModbusFactory();

        CancellationTokenSource cts = new CancellationTokenSource();
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true; // Prevent the process from terminating immediately
            tcs.TrySetResult(true);
        };

        Task terminationTask = tcs.Task;
        Task readDataTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                foreach (string csvLine in csvLines)
                {
                    string[] csvValues = csvLine.Split(',');

                    if (csvValues.Length >= 9)
                    {
                        string portName = csvValues[0]; // Serial port name
                        int baudRate = int.Parse(csvValues[1]); // Baud rate
                        int dataBits = int.Parse(csvValues[2]); // Data bits
                        Parity parity = (Parity)Enum.Parse(typeof(Parity), csvValues[3], true); // Parity
                        StopBits stopBits = (StopBits)Enum.Parse(typeof(StopBits), csvValues[4], true); // Stop bits
                        byte slaveAddress = byte.Parse(csvValues[5]); // Modbus slave address
                        byte modbusFunction = byte.Parse(csvValues[6]); // Modbus function
                        ushort startAddress = ushort.Parse(csvValues[7]); // Start address
                        ushort numRegisters = ushort.Parse(csvValues[8]); // Number of registers

                        using (SerialPort port = new SerialPort(portName, baudRate, parity, dataBits, stopBits))
                        {
                            try
                            {
                                port.Open();
                                Console.WriteLine($"Serial port {portName} connected successfully.");

                                IModbusSerialMaster master = factory.CreateRtuMaster(port);
                                master.Transport.Retries = 3; // Set the number of retries in case of communication failure

                                if (startAddress > 0 && numRegisters > 0 && numRegisters <= 125)
                                {
                                    object? data = null;

                                    if (modbusFunction == 1)
                                    {
                                        bool[] coilData = await master.ReadCoilsAsync(slaveAddress, startAddress, numRegisters);
                                        data = coilData;
                                    }
                                    else if (modbusFunction == 3 || modbusFunction == 4)
                                    {
                                        ushort[] registerData = await master.ReadHoldingRegistersAsync(slaveAddress, startAddress, numRegisters);
                                        data = registerData;
                                    }

                                    if (data != null)
                                    {
                                        string formattedData = FormatData(data, modbusFunction);

                                        // Write the data to MySQL
                                        string connectionString = "server=localhost;port=3306;database=db_tstmod;user=root;password=@dmin1234S";
                                        Write4Columns(connectionString, "tbl_tstplc", "static text #1", "static text #2", csvValues[7], formattedData);
                                    }


                                }
                                else
                                {
                                    Console.WriteLine("Invalid start address or number of registers.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error: {ex.Message}");
                            }
                            finally
                            {
                                port.Close();
                                Console.WriteLine($"Serial port {portName} disconnected.");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid CSV line format.");
                    }
                }

                await Task.Delay(3000); // Delay between each iteration
            }
        });

        await Task.WhenAny(terminationTask, readDataTask);
        cts.Cancel();

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static void Write4Columns(string connectionString, string tablename, string column1, string column2, string column3, string column4)
    {
        using (MySqlConnection connection = new MySqlConnection(connectionString))
        {
            string sql = "INSERT INTO " + tablename + " (column1, column2, column3, column4) VALUES (@column1, @column2, @column3, @column4)";
            MySqlCommand command = new MySqlCommand(sql, connection);

            command.Parameters.AddWithValue("@column1", column1);
            command.Parameters.AddWithValue("@column2", column2);
            command.Parameters.AddWithValue("@column3", column3);
            command.Parameters.AddWithValue("@column4", column4);

            try
            {
                connection.Open();
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to MySQL: {ex.Message}");
            }
        }
    }

    static string FormatData(object data, byte modbusFunction)
    {
        if (modbusFunction == 1) // Read Coil
        {
            bool[] coilData = (bool[])data;
            // Process the coilData array and format it as needed
            // For example, convert bool values to string representation
            return string.Join(",", coilData);
        }
        else if (modbusFunction == 3 || modbusFunction == 4) // Read Holding/Input Registers
        {
            ushort[] registerData = (ushort[])data;
            // Process the registerData array and format it as needed
            // For example, convert ushort values to string representation
            return string.Join(",", registerData);
        }
        else
        {
            Console.WriteLine($"Unsupported Modbus function: {modbusFunction}");
            return string.Empty;
        }
    }
}
