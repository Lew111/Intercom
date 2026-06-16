cmake_minimum_required(VERSION 3.16)

project(InterCom-desktop VERSION 0.1 LANGUAGES CXX)

set(CMAKE_AUTOUIC ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(QT NAMES Qt6 Qt5 REQUIRED COMPONENTS Widgets Core)
find_package(Qt${QT_VERSION_MAJOR} REQUIRED COMPONENTS Widgets Core)

set(PROJECT_SOURCES
        main.cpp
        mainwindow.cpp
        mainwindow.h

)

if(${QT_VERSION_MAJOR} GREATER_EQUAL 6)
    qt_add_executable(InterCom-desktop
        MANUAL_FINALIZATION
        ${PROJECT_SOURCES}
        mainwindow.h mainwindow.cpp
        chatlistmodel.h chatlistmodel.cpp
        messagewidget.h messagewidget.cpp

        dayseparator.h dayseparator.cpp
        smarttextedit.h smarttextedit.cpp
        chatitemdelegate.h chatitemdelegate.cpp
        config.h
        backendmanager.h backendmanager.cpp
        settingsdialog.h settingsdialog.cpp
        aboutdialog.h aboutdialog.cpp
        thememanager.h thememanager.cpp
        logo.qrc
    )
else()
    if(ANDROID)
        add_library(InterCom-desktop SHARED
            ${PROJECT_SOURCES}
        )
    else()
        add_executable(InterCom-desktop
            ${PROJECT_SOURCES}
        )
    endif()
endif()

target_link_libraries(InterCom-desktop PRIVATE Qt${QT_VERSION_MAJOR}::Widgets Qt${QT_VERSION_MAJOR}::Core)

if(${QT_VERSION} VERSION_LESS 6.1.0)
  set(BUNDLE_ID_OPTION MACOSX_BUNDLE_GUI_IDENTIFIER com.example.InterCom-desktop)
endif()
set_target_properties(InterCom-desktop PROPERTIES
    ${BUNDLE_ID_OPTION}
    MACOSX_BUNDLE_BUNDLE_VERSION ${PROJECT_VERSION}
    MACOSX_BUNDLE_SHORT_VERSION_STRING ${PROJECT_VERSION_MAJOR}.${PROJECT_VERSION_MINOR}
    MACOSX_BUNDLE TRUE
    WIN32_EXECUTABLE TRUE
)

include(GNUInstallDirs)
install(TARGETS InterCom-desktop
    BUNDLE DESTINATION .
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

if(QT_VERSION_MAJOR EQUAL 6)
    qt_finalize_executable(InterCom-desktop)
endif()
