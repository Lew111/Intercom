#include <QApplication>
#include <QStyleFactory>
#include <QSettings>
#include <QIcon>
#include "mainwindow.h"
#include "config.h"

int main(int argc, char *argv[])
{
    QApplication a(argc, argv);

    QCoreApplication::setOrganizationName(Config::ORGANIZATION_NAME);
    QCoreApplication::setApplicationName(Config::APPLICATION_NAME);

    QSettings::setDefaultFormat(QSettings::IniFormat);

    a.setStyle(QStyleFactory::create("Fusion"));

    QIcon appIcon(":/new/logo/intercom.png");

    MainWindow w;
    w.show();
    return a.exec();
}