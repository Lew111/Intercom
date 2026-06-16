#include "backendmanager.h"
#include <QDebug>
#include <QThread>
#include <QStringConverter>

BackendManager::BackendManager(QObject* parent)
    : QObject(parent)
    , m_process(new QProcess(this))
    , m_commandTimer(new QTimer(this))
{
    m_commandTimer->setSingleShot(true);

    m_process->setProcessChannelMode(QProcess::MergedChannels);

    connect(m_process, &QProcess::started, this, &BackendManager::onProcessStarted);
    connect(m_process, QOverload<int, QProcess::ExitStatus>::of(&QProcess::finished),
            this, &BackendManager::onProcessFinished);
    connect(m_process, &QProcess::errorOccurred, this, &BackendManager::onProcessError);
    connect(m_process, &QProcess::readyRead, this, &BackendManager::onProcessReadyRead);
    connect(m_commandTimer, &QTimer::timeout, this, &BackendManager::executeNextCommand);

    qDebug() << "BackendManager created";
}

BackendManager::~BackendManager()
{
    stopBackend();
}

void BackendManager::startBackend()
{
    if (isRunning()) {
        qDebug() << "Backend is already running";
        return;
    }

    if (m_backendPath.isEmpty()) {
        emit backendError("Backend path is not set");
        return;
    }

    qDebug() << "Starting backend:" << m_backendPath;

    m_process->start(m_backendPath);

    if (!m_process->waitForStarted(5000)) {
        emit backendError("Failed to start backend process");
        cleanupProcess();
    }
}

void BackendManager::stopBackend()
{
    if (!isRunning()) {
        return;
    }

    qDebug() << "Stopping backend";

    if (m_commandTimer->isActive()) {
        m_commandTimer->stop();
    }

    m_commandQueue.clear();

    m_process->terminate();

    if (!m_process->waitForFinished(3000)) {
        m_process->kill();
        m_process->waitForFinished(1000);
    }

    cleanupProcess();
}

void BackendManager::restartBackend()
{
    qDebug() << "Restarting backend";
    stopBackend();

    QTimer::singleShot(500, this, [this]() {
        startBackend();
    });
}

void BackendManager::sendCommand(const QString& command, int delayMs)
{
    if (!isRunning()) {
        qDebug() << "Cannot send command - backend not running";
        return;
    }

    qDebug() << "Queueing command:" << command << "delay:" << delayMs << "ms";

    m_commandQueue.append(command);

    if (delayMs > 0) {
        m_delays.append(delayMs);
    } else {
        m_delays.append(0);
    }

    if (!m_commandTimer->isActive()) {
        executeNextCommand();
    }
}

void BackendManager::sendCommands(const QStringList& commands, const QList<int>& delays)
{
    if (!isRunning()) {
        qDebug() << "Cannot send commands - backend not running";
        return;
    }



    m_commandQueue.append(commands);

    if (delays.isEmpty()) {
        for (int i = 0; i < commands.size(); ++i) {
            m_delays.append(0);
        }
    } else {
        m_delays.append(delays);
    }

    if (!m_commandTimer->isActive()) {
        executeNextCommand();
    }
}

void BackendManager::onProcessStarted()
{
    qDebug() << "Backend process started";
    m_isReady = false;

    QTimer::singleShot(m_startupDelay, this, [this]() {
        m_isReady = true;
        emit readyStateChanged(true);
        emit backendStarted();
        emit runningStateChanged(true);
        qDebug() << "Backend is ready for commands";
    });
}

void BackendManager::onProcessFinished(int exitCode, QProcess::ExitStatus exitStatus)
{
    qDebug() << "Backend process finished, exit code:" << exitCode
             << "exit status:" << exitStatus;

    cleanupProcess();
    emit runningStateChanged(false);
    emit readyStateChanged(false);
    emit backendStopped();
}

void BackendManager::onProcessError(QProcess::ProcessError error)
{
    qWarning() << "Backend process error:" << error;
    QString errorStr = "Process error: " + QString::number(error);
    emit backendError(errorStr);
}

void BackendManager::onProcessReadyRead()
{
    QByteArray output = m_process->readAll();
    QString outputStr;


    outputStr = QString::fromUtf8(output);


    emit consoleOutput(outputStr);

    QStringList lines = outputStr.split('\n', Qt::SkipEmptyParts);
    for (const QString& line : lines) {
        QString trimmedLine = line.trimmed();
        qDebug() << "Backend output:" << trimmedLine;

        if (trimmedLine.contains("READY") || trimmedLine.contains("ready") ||
            trimmedLine.contains("listening") || trimmedLine.contains("started")) {
            if (!m_isReady) {
                m_isReady = true;
                emit readyStateChanged(true);
                qDebug() << "Backend is now ready (detected READY marker)";
            }
        }
    }

    checkReadiness();
}

void BackendManager::executeNextCommand()
{
    if (m_commandQueue.isEmpty()) {
        emit allCommandsCompleted();
        return;
    }

    QString command = m_commandQueue.takeFirst();
    int delay = m_delays.isEmpty() ? 0 : m_delays.takeFirst();

    qDebug() << "Sending command to backend:" << command;

    if (m_process && m_process->state() == QProcess::Running) {
        QByteArray commandBytes;


        commandBytes = (command + "\n").toUtf8();
        m_process->write(commandBytes);
        m_process->waitForBytesWritten(1000);

        emit commandSent(command);
    }

    if (!m_commandQueue.isEmpty()) {
        int nextDelay = m_delays.first();
        if (nextDelay > 0) {
            m_commandTimer->start(nextDelay);
        } else {
            QTimer::singleShot(10, this, &BackendManager::executeNextCommand);
        }
    } else {
        emit allCommandsCompleted();
    }
}

void BackendManager::checkReadiness()
{
    static QByteArray accumulatedOutput;
    accumulatedOutput += m_process->readAll();

    QString output = QString::fromLocal8Bit(accumulatedOutput);

    if (output.contains("READY") || output.contains("ready") ||
        output.contains("listening") || output.contains("started")) {

        if (!m_isReady) {
            m_isReady = true;
            emit readyStateChanged(true);
            qDebug() << "Backend is now ready (detected READY marker)";
        }
    }

    if (accumulatedOutput.size() > 4096) {
        accumulatedOutput.clear();
    }
}

void BackendManager::cleanupProcess()
{
    m_isReady = false;
    m_commandQueue.clear();
    m_delays.clear();

    if (m_commandTimer->isActive()) {
        m_commandTimer->stop();
    }
}
