using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.IO.Compression;
using System.Net;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System;
using Dummiesman;


public class NewBehaviourScript : MonoBehaviour
{
    public Camera camera;
    public string savePath = "/Users/appleuser/Desktop/bordeaux/ManagementIT/test"; // Ruta local donde guardarás las fotos
    public string zipFilePath = "/Users/appleuser/Desktop/bordeaux/ManagementIT/test/photos.zip"; // Ruta del archivo zip
    public string ip = "172.20.10.7:7026"; // Ruta en la red donde enviarás el archivo
    public string watchPath = "/Users/appleuser/Downloads/models"; // Ruta donde se monitorean los archivos .obj
    public float distanceFromCamera = 5f; // Distancia frente a la cámara
    public float scaleFactor = 0.7f; // Escala para redimensionar el objeto
    public float verticalOffset = -1.0f; // Ajuste vertical para la posición del objeto

    private FileSystemWatcher watcher;
    private HashSet<string> loadedModels = new HashSet<string>(); // Almacenar los modelos cargados

    void Start()
    {
        if (camera == null)
        {
            camera = Camera.main; // Asigna la cámara principal automáticamente si no se ha asignado
        }

        // Configurar el Watcher para monitorear la carpeta de modelos .obj
        SetupFileWatcher();

        // Revisar los archivos actuales en la carpeta al inicio
        LoadExistingModels();
    }

    void Update()
    {
        // En cada frame, revisar si hay nuevos archivos .obj
        CheckForNewModels();
    }

    // Configurar el Watcher para detectar nuevos archivos .obj
    void SetupFileWatcher()
    {
        watcher = new FileSystemWatcher(watchPath);
        watcher.Filter = "*.obj";
        watcher.Created += OnNewModelDetected; // Evento cuando se detecta un nuevo archivo .obj
        watcher.EnableRaisingEvents = true; // Habilitar la detección de eventos
    }

    // Método que se ejecuta cuando se detecta un nuevo archivo .obj
    void OnNewModelDetected(object sender, FileSystemEventArgs e)
    {
        if (e.Name.EndsWith(".obj") && !e.Name.Contains("READED"))
        {
            LoadModel(e.FullPath);
        }
    }

    // Cargar todos los modelos .obj existentes en la carpeta al inicio
    void LoadExistingModels()
    {
        string[] files = Directory.GetFiles(watchPath, "*.obj");

        foreach (string file in files)
        {
            if (!file.Contains("READED") && !loadedModels.Contains(file)) // Evitar cargar modelos ya procesados
            {
                LoadModel(file);
            }
        }
    }

    // Verificar y cargar modelos nuevos en cada frame
    void CheckForNewModels()
    {
        LoadExistingModels();
    }

    // Cargar y procesar un modelo .obj
    void LoadModel(string filePath)
    {
        Debug.Log($"Cargando modelo: {filePath}");

        // Cargar el modelo .obj
        GameObject loadedObject = LoadObjFile(filePath);

        if (loadedObject != null)
        {
             Debug.Log($"Locating in scene . . .");
            // Redimensionar y posicionar el objeto
            ResizeObject(loadedObject, scaleFactor);
            PositionObjectInFrontOfCamera(loadedObject);

            // Renombrar el archivo para marcarlo como leído
            string newFileName = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + "_READED.obj");
            File.Move(filePath, newFileName);

            // Marcar el modelo como cargado
            loadedModels.Add(filePath);

            // Tomar fotos y enviarlas por ZIP
            CaptureAndSendPhotos();
        }
        else
        {
            Debug.LogError("No se pudo cargar el archivo .obj.");
        }
    }

    // Cargar un archivo .obj
    GameObject LoadObjFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"El archivo {filePath} no existe.");
            return null;
        }

        // Usar un cargador adecuado para cargar el archivo .obj (aquí puedes utilizar un plugin como OBJLoader)
        Debug.Log($"File path {filePath} . . .");
        OBJLoader objLoader = new OBJLoader();
        return objLoader.Load(filePath);
    }

    // Redimensionar un objeto
    void ResizeObject(GameObject obj, float factor)
    {
        obj.transform.localScale *= factor;
    }

    // Posicionar el objeto frente a la cámara
    void PositionObjectInFrontOfCamera(GameObject obj)
    {
        Vector3 cameraPosition = camera.transform.position;
        Vector3 cameraForward = camera.transform.forward;

        Vector3 targetPosition = cameraPosition + cameraForward * 25;
        targetPosition.y += verticalOffset;

        obj.transform.position = targetPosition;
        obj.transform.LookAt(camera.transform);
    }

    // Método para capturar 3 fotos y enviarlas por ZIP
    void CaptureAndSendPhotos()
    {
        // Captura de fotos
        CapturePhotos();

        // Crear archivo ZIP con las fotos
        CreateZipFile();

        // Enviar el archivo ZIP
        SendToLaptop();
    }

    // Capturar 3 fotos de la escena
    void CapturePhotos()
    {
        for (int i = 1; i <= 3; i++)
        {
            RenderTexture renderTexture = new RenderTexture(256, 256, 24);
            camera.targetTexture = renderTexture;
            Texture2D photo = new Texture2D(256, 256, TextureFormat.RGB24, false);
            camera.Render();

            RenderTexture.active = renderTexture;
            photo.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            photo.Apply();

            byte[] bytes = photo.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(savePath, $"photo{i}.png"), bytes);

            camera.targetTexture = null;
            RenderTexture.active = null;
            Destroy(renderTexture);
        }
    }

    // Crear archivo ZIP con las fotos
    void CreateZipFile()
    {
        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath); // Eliminar el archivo ZIP anterior si existe
        }

        try
        {
            ZipFile.CreateFromDirectory(savePath, zipFilePath); // Comprimir las fotos en un archivo ZIP
        }
        catch (Exception e)
        {
            Debug.LogError($"Error al crear el archivo ZIP: {e.Message}");
            SendToLaptop();
        }
    }

    // Enviar el archivo ZIP a la laptop
    async Task SendToLaptop()
    {
        string url = $"https://{ip}/WeatherForecast/ZipReceiver"; // Use HTTPS

        // Ignore SSL certificate validation
        HttpClientHandler handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; }
        };

        using (var client = new HttpClient(handler))
        {
            using (var content = new MultipartFormDataContent())
            {
                byte[] fileBytes = File.ReadAllBytes(zipFilePath);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
                content.Add(fileContent, "file", "photos.zip");

                // Add the filename as a header
                content.Headers.Add("X-File-Name", Path.GetFileName(zipFilePath));  // Here we add the file name

                try
                {
                    HttpResponseMessage response = await client.PostAsync(url, content);
                    if (response.IsSuccessStatusCode)
                    {
                        Debug.Log("File sent successfully.");
                    }
                    else
                    {
                        Debug.LogError($"Error sending file: {response.StatusCode}");
                    }
                }
                catch (HttpRequestException e)
                {
                    Debug.LogError($"Error in the HTTP request: {e.Message}");
                }
            }
        }
    }
}
